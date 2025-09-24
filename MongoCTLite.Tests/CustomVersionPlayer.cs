using MongoCTLite.Tracking;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoCTLite.Tests;

[MongoTrackedEntity]
public sealed class CustomVersionPlayer
{
    [BsonId]
    [MongoIdField]
    public ObjectId Id { get; set; }

    [BsonElement("revision")]
    [MongoVersionField]
    public long Revision { get; set; }

    public int Level { get; set; }
    public long Gold { get; set; }
}
