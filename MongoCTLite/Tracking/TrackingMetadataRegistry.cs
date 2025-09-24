using System;
using System.Collections.Concurrent;

namespace MongoCTLite.Tracking;

public static class TrackingMetadataRegistry
{
    private static readonly ConcurrentDictionary<Type, TrackingMetadata> _metadata = new();

    public static void Register(Type type, string idField, string versionField)
    {
        _metadata[type] = new TrackingMetadata(idField, versionField);
    }

    internal static TrackingMetadata GetOrThrow(Type type)
    {
        if (_metadata.TryGetValue(type, out var metadata))
            return metadata;

        throw new InvalidOperationException($"Type '{type.FullName}' is not marked with [MongoTrackedEntity].");
    }
}

internal readonly record struct TrackingMetadata(string IdField, string VersionField);
