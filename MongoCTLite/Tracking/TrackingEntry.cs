using System;
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
    
    internal string       IdField      { get; }
    internal string       VersionField { get; }
    internal BsonDocument Original     { get; }

    public TrackingEntry(
        IMongoCollection<T> col,
        T entity,
        long? expectedVersion = null,
        string idField = "_id",
        string versionField = "version")
    {
        Collection     = col;
        BsonCollection = col.Database.GetCollection<BsonDocument>(col.CollectionNamespace.CollectionName);
        Current        = entity;
        IdField        = idField;
        VersionField   = versionField;

        if (string.Equals(IdField, VersionField, StringComparison.Ordinal))
            throw new InvalidOperationException("Id field and version field cannot be the same.");

        Original = entity.ToBsonDocument();

        // Enhanced _id validation
        if (!Original.TryGetValue(IdField, out var id))
            throw new InvalidOperationException($"Entity of type {typeof(T).Name} must have an `{IdField}` field");
        Id = id;

        var version = expectedVersion ?? TryGetVersion(Original, VersionField);
        if (version is null)
            throw new InvalidOperationException($"Document must contain a numeric `{VersionField}` field.");

        ExpectedVersion  = version;
    }

    private static long? TryGetVersion(BsonDocument doc, string versionField)
    {
        if (!doc.TryGetValue(versionField, out var v))
            return null;

        return v.BsonType switch
        {
            BsonType.Int32 => v.AsInt32,
            BsonType.Int64 => v.AsInt64,
            _              => null
        };
    }
}
