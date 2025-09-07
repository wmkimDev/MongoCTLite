namespace MongoCTLite.Tracking;

public sealed class SaveChangesOptions
{
    public bool Ordered        { get; init; } = false; // BulkWriteOptions.IsOrdered
    public int  MaxRetries     { get; init; } = 2;     // 일시적 오류 재시도 횟수
    public bool UseTransaction { get; init; } = false; // 멀티 문서 원자 필요 시만 true
}