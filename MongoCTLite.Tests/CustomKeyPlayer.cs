using MongoCTLite.Tracking;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoCTLite.Tests;

[MongoTrackedEntity]
public sealed class CustomKeyPlayer
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("playerId")]
    [MongoIdField]
    public string PlayerId { get; set; } = string.Empty;

    [MongoVersionField]
    public long version { get; set; }

    public int score { get; set; }
}
