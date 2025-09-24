namespace MongoCTLite.Tracking;

public sealed class SaveChangesOptions
{
    public bool Ordered        { get; init; } = false; // BulkWriteOptions.IsOrdered
    public int  MaxRetries     { get; init; } = 2;     // Number of retries for transient errors
    public bool UseTransaction { get; init; } = false; // Set to true only if multi-document atomicity is required
}
