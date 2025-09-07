using MongoCTLite.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoCTLite.Tracking;

// 제네릭 TrackingEntry<T> → 비제네릭 어댑터
internal sealed class TrackingEntryAdapter<T> : ITrackingEntry
{
    private readonly TrackingEntry<T> _inner;
    public TrackingEntryAdapter(TrackingEntry<T> inner) => _inner = inner;

    public IMongoCollection<BsonDocument> BsonCollection     => _inner.BsonCollection;
    public string                         CollectionFullName => _inner.BsonCollection.CollectionNamespace.FullName;
    public BsonValue                      Id                 => _inner.Id;

    public WriteModel<BsonDocument>? BuildModel(DiffPolicy policy)
        => MongoCTLite.Diff.DiffEngine.BuildModel(_inner, policy);
}