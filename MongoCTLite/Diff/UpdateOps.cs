using MongoDB.Bson;

namespace MongoCTLite.Diff;

/// <summary>
/// MongoDB Update operation container
/// </summary>
public sealed class UpdateOps
{
    // Field Operations
    public Dictionary<string, BsonValue> Sets   { get; } = new();
    public HashSet<string>               Unsets { get; } = new();
    public Dictionary<string, NumericDelta> Incs { get; } = new();

    // Array Operations
    public Dictionary<string, List<BsonValue>> Pushes { get; } = new();
    public Dictionary<string, List<BsonValue>> Pulls  { get; } = new();

    public int  FieldOpsCount => Sets.Count + Unsets.Count + Incs.Count + Pushes.Count + Pulls.Count;
    public bool IsEmpty       => FieldOpsCount == 0 && Pushes.Count == 0 && Pulls.Count == 0;
}

public readonly struct NumericDelta
{
    public NumericDelta(long value, BsonType type)
    {
        Value = value;
        Type  = type;
    }

    public long     Value { get; }
    public BsonType Type  { get; }

    public NumericDelta Add(long value) => new(Value + value, Type);
}
