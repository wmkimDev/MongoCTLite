using MongoCTLite.Abstractions;
using MongoDB.Driver;

namespace MongoCTLite.Tracking;

/// <summary>
/// Unit of work context that collects multiple entities and flushes them at once
/// </summary>
public interface ITrackingContext
{
    /// <summary>
    /// Attach an entity (start snapshot tracking). The entity type must be annotated with
    /// <see cref="MongoTrackedEntityAttribute"/> and define members marked with
    /// <see cref="MongoIdFieldAttribute"/> and <see cref="MongoVersionFieldAttribute"/>.
    /// </summary>
    void Attach<T>(IMongoCollection<T> col, T entity, long? expectedVersion = null);

    /// <summary>
    /// Flush changes (calls DiffEngine → BulkWrite)
    /// </summary>
    Task<int> SaveChangesAsync(DiffPolicy policy, IRunLogger log, CancellationToken ct = default);
}
