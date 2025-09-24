using MongoCTLite.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoCTLite.Tracking;

internal sealed class TrackingEntryAdapter<T> : ITrackingEntry
{
    private readonly TrackingEntry<T> _inner;
    public TrackingEntryAdapter(TrackingEntry<T> inner) => _inner = inner;

    public IMongoCollection<BsonDocument> BsonCollection     => _inner.BsonCollection;
    public string                         CollectionFullName => _inner.BsonCollection.CollectionNamespace.FullName;
    public BsonValue                      Id                 => _inner.Id;
    public T Current => _inner.Current;

    public WriteModel<BsonDocument>? BuildModel(DiffPolicy policy)
        => MongoCTLite.Diff.DiffEngine.BuildModel(_inner, policy);
}
