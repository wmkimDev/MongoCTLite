using System;
using MongoCTLite.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoCTLite.Tracking;

public sealed class TrackingContext : ITrackingContext
{
    // Same request scope only. Do not share across multiple threads.
    private readonly List<ITrackingEntry> _entries = new();

    // (Type, Id) -> Entry index for fast lookup
    private readonly Dictionary<EntryKey, ITrackingEntry> _index = new();

    private readonly record struct EntryKey(Type Type, BsonValue Id);

    private static EntryKey KeyOf<T>(BsonValue id) => new(typeof(T), id);
    private static BsonValue NormalizeId(object id)
    {
        if (id is null)
            throw new ArgumentNullException(nameof(id));

        return id is BsonValue bson ? bson : BsonValue.Create(id);
    }

    public void Attach<T>(IMongoCollection<T> col, T entity, long? expectedVersion = null)
    {
        var metadata = TrackingMetadataRegistry.GetOrThrow(typeof(T));
        var wrapped = new TrackingEntryAdapter<T>(new TrackingEntry<T>(col, entity, expectedVersion, metadata.IdField, metadata.VersionField));
        var key = KeyOf<T>(wrapped.Id);

        if (_index.ContainsKey(key))
            throw new InvalidOperationException(
                $"Already attached in this context: {wrapped.CollectionFullName} id={wrapped.Id}");

        _entries.Add(wrapped);
        _index[key] = wrapped;
    }

    public T GetTrackedEntity<T>(object id)
    {
        if (TryGetTrackedEntity<T>(id, out var entity))
            return entity;

        throw new InvalidOperationException(
            $"Entity of type {typeof(T).Name} with id={id} is not being tracked in the current context.");
    }

    public bool TryGetTrackedEntity<T>(object id, out T entity)
    {
        var key = KeyOf<T>(NormalizeId(id));
        if (_index.TryGetValue(key, out var e))
        {
            entity = ((TrackingEntryAdapter<T>)e).Current;
            return true;
        }
        entity = default!;
        return false;
    }

    public Task<int> SaveChangesAsync(DiffPolicy policy, IRunLogger log, CancellationToken ct = default)
        => SaveChangesAsync(policy, log, new SaveChangesOptions(), ct);

    public async Task<int> SaveChangesAsync(
        DiffPolicy policy,
        IRunLogger log,
        SaveChangesOptions options,
        CancellationToken ct = default)
    {
        if (_entries.Count == 0)
            return 0;

        // Block duplicate updates to the same document in one context
        var seen = new HashSet<(string Col, BsonValue Id)>();
        var modelsByCol = new Dictionary<IMongoCollection<BsonDocument>, List<WriteModel<BsonDocument>>>();

        foreach (var entry in _entries)
        {
            var model = entry.BuildModel(policy);
            if (model is null) continue;

            var dupKey = (entry.CollectionFullName, entry.Id);
            if (!seen.Add(dupKey))
                throw new InvalidOperationException($"Duplicated update in this context: {entry.CollectionFullName} id={entry.Id}");

            if (!modelsByCol.TryGetValue(entry.BsonCollection, out var list))
            {
                list = new List<WriteModel<BsonDocument>>();
                modelsByCol.Add(entry.BsonCollection, list);
            }
            list.Add(model);
        }

        if (modelsByCol.Count == 0)
        {
            ClearState();
            return 0;
        }

        var totalReq = 0;
        var totalMatched = 0;

        IClientSessionHandle? session = null;

        try
        {
            if (options.UseTransaction)
            {
                var anyCol = modelsByCol.Keys.First(); // 동일 클러스터 가정
                session = await anyCol.Database.Client.StartSessionAsync(cancellationToken: ct);
                session.StartTransaction();
            }

            foreach (var (col, models) in modelsByCol)
            {
                ct.ThrowIfCancellationRequested();

                var res = await DoBulkWithRetry(
                    col, session, models,
                    new BulkWriteOptions { IsOrdered = options.Ordered },
                    options.MaxRetries, log, ct);

                totalReq     += models.Count;
                totalMatched += (int)res.MatchedCount;

                log.Info($"BulkWrite OK: {col.CollectionNamespace.CollectionName} req={models.Count} matched={res.MatchedCount} modified={res.ModifiedCount}");
            }

            if (options.UseTransaction && session is not null)
                await session.CommitTransactionAsync(ct);

            var conflicts = totalReq - totalMatched;
            if (conflicts > 0)
                throw new ConcurrencyConflictException(conflicts, $"Optimistic concurrency conflict: {conflicts} item(s) not matched by _id+version.");

            return totalReq;
        }
        catch (Exception ex)
        {
            if (options.UseTransaction && session is not null)
            {
                try { await session.AbortTransactionAsync(ct); } catch { /* ignore */ }
            }
            log.Error(ex, "SaveChanges failed");
            throw;
        }
        finally
        {
            session?.Dispose();
            ClearState();
        }
    }

    private void ClearState()
    {
        _entries.Clear();
        _index.Clear();
    }

    private static async Task<BulkWriteResult<BsonDocument>> DoBulkWithRetry(
        IMongoCollection<BsonDocument> col,
        IClientSessionHandle? session,
        IEnumerable<WriteModel<BsonDocument>> models,
        BulkWriteOptions opts,
        int maxRetries,
        IRunLogger log,
        CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(80);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return session is null
                    ? await col.BulkWriteAsync(models, opts, ct).ConfigureAwait(false)
                    : await col.BulkWriteAsync(session, models, opts, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxRetries)
            {
                log.Warn($"Transient error, retry {attempt + 1}/{maxRetries}: {ex.GetType().Name}");
                await Task.Delay(delay, ct);
                delay += delay;
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is MongoConnectionException
        || ex is MongoNotPrimaryException
        || ex is MongoNodeIsRecoveringException
        || ex is MongoExecutionTimeoutException
        || ex is MongoWriteConcernException;
}
