using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoCTLite.Tracking;

public sealed class TrackingEntry<T>
{
    public IMongoCollection<T>            Collection      { get; }
    public IMongoCollection<BsonDocument> BsonCollection  { get; }
    public T                              Current         { get; }
    public BsonValue                      Id              { get; }
    public long?                          ExpectedVersion { get; }
    
    internal string       IdField  { get; }
    internal BsonDocument Original { get; }

    public TrackingEntry(IMongoCollection<T> col, T entity, long? expectedVersion = null, string idField = "_id")
    {
        Collection     = col;
        BsonCollection = col.Database.GetCollection<BsonDocument>(col.CollectionNamespace.CollectionName);
        Current        = entity;
        IdField       = idField;

        Original = entity.ToBsonDocument();

        // Enhanced _id validation
        if (!Original.TryGetValue(IdField, out var id))
            throw new InvalidOperationException($"Entity of type {typeof(T).Name} must have an `{IdField}` field");
        Id = id;
        
        var version = expectedVersion ?? TryGetVersion(Original);
        if (version is null)
            throw new InvalidOperationException("Document must contain a numeric `version` field.");

        ExpectedVersion = version;
    }
    
    private static long? TryGetVersion(BsonDocument doc)
    {
        if (!doc.TryGetValue("version", out var v))
            return null;
            
        return v.BsonType switch
        {
            BsonType.Int32 => v.AsInt32,
            BsonType.Int64 => v.AsInt64,
            _              => null
        };
    }
}