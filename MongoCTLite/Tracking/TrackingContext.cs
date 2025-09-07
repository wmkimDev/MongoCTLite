using MongoCTLite.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoCTLite.Tracking;

public sealed class TrackingContext : ITrackingContext
{
    // Same request scope only. Do not share across multiple threads.
    private readonly List<ITrackingEntry> _entries = new();

    public TrackingEntry<T> Attach<T>(IMongoCollection<T> col, T entity, long? expectedVersion = null)
    {
        var e = new TrackingEntry<T>(col, entity, expectedVersion);
        _entries.Add(new TrackingEntryAdapter<T>(e));
        return e;
    }

    public async Task<int> SaveChangesAsync(DiffPolicy policy, IRunLogger log, CancellationToken ct = default)
        => await SaveChangesAsync(policy, log, new SaveChangesOptions(), ct);

    public async Task<int> SaveChangesAsync(
        DiffPolicy policy,
        IRunLogger log,
        SaveChangesOptions options,
        CancellationToken ct = default)
    {
        if (_entries.Count == 0)
            return 0;

        // Same document cannot be updated multiple times within the same context.
        var seen = new HashSet<(string Col, string IdStr)>();
        var modelsByCol = new Dictionary<IMongoCollection<BsonDocument>, List<WriteModel<BsonDocument>>>();

        foreach (var entry in _entries)
        {
            var model = entry.BuildModel(policy);
            if (model is null)
                continue;

            var key = (entry.CollectionFullName, entry.Id.ToString());
            if (!seen.Add(key!))
                throw new InvalidOperationException($"The same document cannot be updated multiple times within the same context: {key.CollectionFullName}");
            
            if (!modelsByCol.TryGetValue(entry.BsonCollection, out var list))
            {
                list = new List<WriteModel<BsonDocument>>();
                modelsByCol.Add(entry.BsonCollection, list);
            }
            list.Add(model);
        }

        if (modelsByCol.Count == 0)
        {
            _entries.Clear();
            return 0;
        }

        var totalReq     = 0;
        var totalMatched = 0;

        IClientSessionHandle? session = null;
        try
        {
            if (options.UseTransaction)
            {
                var anyCol = modelsByCol.Keys.First(); // 같은 클러스터라는 전제
                session = await anyCol.Database.Client.StartSessionAsync(cancellationToken: ct);
                session.StartTransaction();
            }

            foreach (var (col, models) in modelsByCol)
            {
                ct.ThrowIfCancellationRequested();

                var opts = new BulkWriteOptions { IsOrdered = options.Ordered };

                var res = await DoBulkWithRetry(col, session, models, opts, options.MaxRetries, log, ct);

                totalReq     += models.Count;
                totalMatched += (int)res.MatchedCount;

                log.Info($"BulkWrite OK: {col.CollectionNamespace.CollectionName} req={models.Count} matched={res.MatchedCount} modified={res.ModifiedCount}");
            }

            if (options.UseTransaction && session is not null)
                await session.CommitTransactionAsync(ct);

            // Strict: _id+version 필터 미매치는 논리적 충돌
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
            log.Error(ex, "SaveChanges 실패");
            throw;
        }
        finally
        {
            session?.Dispose();
            _entries.Clear(); 
        }
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
                if (session is null)
                    return await col.BulkWriteAsync(models, opts, ct).ConfigureAwait(false);
                else
                    return await col.BulkWriteAsync(session, models, opts, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxRetries)
            {
                log.Warn($"Transient error detected, retrying {attempt + 1}/{maxRetries}: {ex.GetType().Name}");
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
