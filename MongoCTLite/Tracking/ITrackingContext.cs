using MongoCTLite.Abstractions;
using MongoDB.Driver;

namespace MongoCTLite.Tracking;

/// <summary>
/// Unit of work context that collects multiple entities and flushes them at once
/// </summary>
public interface ITrackingContext
{
    /// <summary>
    /// Attach an entity (start snapshot tracking)
    /// </summary>
    void Attach<T>(IMongoCollection<T> col, T entity, long? expectedVersion = null);

    /// <summary>
    /// Flush changes (calls DiffEngine → BulkWrite)
    /// </summary>
    Task<int> SaveChangesAsync(DiffPolicy policy, IRunLogger log, CancellationToken ct = default);
}