using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoCTLite.Tests;

public sealed class Player
{
    [BsonId] public ObjectId Id { get; set; }
    public long version { get; set; }
    public int level { get; set; }
    public long gold { get; set; }
    public List<string> items { get; set; } = new();
}