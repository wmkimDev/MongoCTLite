using MongoCTLite.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoCTLite.Tracking;

internal interface ITrackingEntry
{
    IMongoCollection<BsonDocument> BsonCollection     { get; }
    string                         CollectionFullName { get; }
    BsonValue                      Id                 { get; }
    WriteModel<BsonDocument>? BuildModel(DiffPolicy policy);
}
