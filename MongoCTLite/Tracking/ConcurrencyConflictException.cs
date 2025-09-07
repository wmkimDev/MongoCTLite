namespace MongoCTLite.Tracking;

public sealed class ConcurrencyConflictException(int conflicts, string message) : Exception(message)
{
    public int ConflictCount { get; } = conflicts;
}