using MongoCTLite.Abstractions;
using MongoDB.Driver;

namespace MongoCTLite.Tracking;

/// <summary>
/// 여러 엔티티를 모아뒀다가 한 번에 flush하는 단위 작업 컨텍스트
/// </summary>
public interface ITrackingContext
{
    /// <summary>
    /// 엔티티 attach (스냅샷 보관 시작)
    /// </summary>
    TrackingEntry<T> Attach<T>(IMongoCollection<T> col, T entity, long? expectedVersion = null);

    /// <summary>
    /// 변경사항 flush (DiffEngine 호출 → BulkWrite)
    /// </summary>
    Task<int> SaveChangesAsync(DiffPolicy policy, IRunLogger log, CancellationToken ct = default);
}