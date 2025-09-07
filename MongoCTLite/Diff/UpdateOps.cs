using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoCTLite.Diff;

/// <summary>
/// MongoDB Update 연산 컨테이너
/// </summary>
public sealed class UpdateOps
{
    // 필드 연산
    public Dictionary<string, BsonValue> Sets   { get; } = new();
    public HashSet<string>               Unsets { get; } = new();
    public Dictionary<string, long>      Incs   { get; } = new();
    
    // 배열 연산
    public Dictionary<string, List<BsonValue>> Pushes { get; } = new();
    public Dictionary<string, List<BsonValue>> Pulls  { get; } = new();
    
    public int  FieldOpsCount => Sets.Count + Unsets.Count + Incs.Count;
    public bool IsEmpty       => FieldOpsCount == 0 && Pushes.Count == 0 && Pulls.Count == 0;
}