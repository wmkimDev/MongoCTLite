using MongoDB.Bson;
using MongoDB.Driver;
using MongoCTLite.Abstractions;
using MongoCTLite.Tracking;
using static MongoCTLite.Diff.PathUtils;
using static MongoCTLite.Diff.BsonUtils;
using static MongoCTLite.Diff.ArrayUtils;

namespace MongoCTLite.Diff;

public static class DiffEngine
{
    public static WriteModel<BsonDocument>? BuildModel<T>(
        TrackingEntry<T> e, 
        DiffPolicy policy)
    {
        var original = e.Original;
        var current = e.Current!.ToBsonDocument();
        
        if (current.TryGetValue(e.IdField, out var incomingId) && incomingId != e.Id)
            throw new InvalidOperationException($"`{e.IdField}` field cannot be modified.");
        
        
        var reservedRoot = new HashSet<string> { e.IdField, e.VersionField };
        
        var ops = ComputeDiff(original, current, policy, reservedRoot);
        
        if (ops.IsEmpty)
            return null;
        
        if (ops.FieldOpsCount > policy.MaxFieldOpsBeforeDocReplace)
        {
            return CreateReplaceModel(e, current);
        }
        
        return CreateUpdateModel(e, ops);
    }
    
    private static UpdateOps ComputeDiff(
        BsonDocument original, 
        BsonDocument current, 
        DiffPolicy policy,
        ISet<string> reservedRoot)
    {
        var ops = new UpdateOps();
        DiffDocument("", original, current, ops, policy, reservedRoot);
        return ops;
    }
    
    private static void DiffDocument(
        string prefix,
        BsonDocument original,
        BsonDocument current,
        UpdateOps ops,
        DiffPolicy policy,
        ISet<string> reservedRoot)
    {
        var originalKeys = original.Names.ToHashSet();
        var currentKeys = current.Names.ToHashSet();
        
        if (prefix.Length == 0)
        {
            originalKeys.ExceptWith(reservedRoot);
            currentKeys.ExceptWith(reservedRoot);
        }
        
        // Deleted fields
        foreach (var key in originalKeys.Except(currentKeys))
        {
            var path = Join(prefix, key);
            
            if (policy.AllowUnset(path))
                ops.Unsets.Add(path);
            else
                ops.Sets[path] = BsonNull.Value;
        }
        
        // Current fields
        foreach (var key in currentKeys)
        {
            var path = Join(prefix, key);
            var currentValue = current[key];
            
            if (!original.TryGetValue(key, out var originalValue))
            {
                ops.Sets[path] = currentValue;
                continue;
            }
            
            DiffValue(path, originalValue, currentValue, ops, policy, reservedRoot);
        }
    }

    private static void DiffValue(
        string path,
        BsonValue original,
        BsonValue current,
        UpdateOps ops,
        DiffPolicy policy,
        ISet<string> reservedRoot)
    {
        if (Equals(original, current))
            return;
        
        switch ((original.BsonType, current.BsonType))
        {
            case (BsonType.Document, BsonType.Document):
                DiffDocument(path, original.AsBsonDocument, current.AsBsonDocument, ops, policy, reservedRoot);
                break;
            
            case (BsonType.Array, BsonType.Array):
                DiffArray(path, original.AsBsonArray, current.AsBsonArray, ops);
                break;
            
            case var types when AreBothNumeric(original, current):
                if (policy.AllowInc(path) && TryComputeIncrement(original, current, out var numericDelta))
                {
                    if (numericDelta.Value != 0)
                    {
                        if (ops.Incs.TryGetValue(path, out var existing))
                        {
                            if (existing.Type != numericDelta.Type)
                            {
                                ops.Incs.Remove(path);
                                ops.Sets[path] = current;
                            }
                            else
                            {
                                ops.Incs[path] = existing.Add(numericDelta.Value);
                            }
                        }
                        else
                        {
                            ops.Incs[path] = numericDelta;
                        }
                    }
                }
                else
                {
                    ops.Sets[path] = current;
                }
                break;
            
            case (_, BsonType.Null):
                if (policy.AllowUnset(path))
                    ops.Unsets.Add(path);
                else
                    ops.Sets[path] = BsonNull.Value;
                break;
            
            default:
                ops.Sets[path] = current;
                break;
        }
    }
    
    private static void DiffArray(
        string path,
        BsonArray original,
        BsonArray current,
        UpdateOps ops)
    {
        // Append-only
        if (IsAppendOnly(original, current))
        {
            var appended = GetAppendedElements(original, current);
            if (appended.Count > 0)
                ops.Pushes[path] = appended;
            return;
        }

        // Remove-only
        if (IsRemoveOnly(original, current))
        {
            var removed = GetRemovedElements(original, current);
            if (removed.Count > 0)
                ops.Pulls[path] = removed;
            return;
        }

        // Mixed changes → replace the whole array for deterministic behaviour
        ops.Sets[path] = current;
    }
    
    private static WriteModel<BsonDocument> CreateReplaceModel<T>(
        TrackingEntry<T> e, BsonDocument current)
    {
        var f = Builders<BsonDocument>.Filter;

        if (current.TryGetValue(e.IdField, out var incomingId) && incomingId != e.Id)
            throw new InvalidOperationException($"`{e.IdField}` field cannot be modified.");

        var replacement = new BsonDocument();
        foreach (var el in current)
        {
            if (el.Name == e.IdField || el.Name == e.VersionField) continue;
            replacement.Add(el);
        }

        replacement[e.IdField] = e.Id;
        replacement[e.VersionField] = e.ExpectedVersion!.Value + 1;

        var filter = f.Eq(e.IdField, e.Id) & f.Eq(e.VersionField, e.ExpectedVersion!.Value);
        return new ReplaceOneModel<BsonDocument>(filter, replacement) { IsUpsert = false };
    }

    private static UpdateOneModel<BsonDocument> CreateUpdateModel<T>(
        TrackingEntry<T> e, UpdateOps ops)
    {
        var updates = new List<UpdateDefinition<BsonDocument>>();

        foreach (var (path, value) in ops.Sets)
            updates.Add(Builders<BsonDocument>.Update.Set(path, value));

        foreach (var path in ops.Unsets)
            updates.Add(Builders<BsonDocument>.Update.Unset(path));

        foreach (var (path, delta) in ops.Incs)
        {
            updates.Add(delta.Type == BsonType.Int32
                ? Builders<BsonDocument>.Update.Inc(path, checked((int)delta.Value))
                : Builders<BsonDocument>.Update.Inc(path, delta.Value));
        }

        foreach (var (path, values) in ops.Pushes)
            updates.Add(values.Count == 1
                ? Builders<BsonDocument>.Update.Push(path, values[0])
                : Builders<BsonDocument>.Update.PushEach(path, values));

        foreach (var (path, values) in ops.Pulls)
            updates.Add(values.Count == 1
                ? Builders<BsonDocument>.Update.Pull(path, values[0])
                : Builders<BsonDocument>.Update.PullAll(path, values));

        updates.Add(Builders<BsonDocument>.Update.Inc(e.VersionField, 1));

        var f      = Builders<BsonDocument>.Filter;
        var filter = f.Eq(e.IdField, e.Id) & f.Eq(e.VersionField, e.ExpectedVersion!.Value);

        return new UpdateOneModel<BsonDocument>(filter, Builders<BsonDocument>.Update.Combine(updates))
        {
            IsUpsert = false
        };
}

    private static bool TryComputeIncrement(BsonValue original, BsonValue current, out NumericDelta delta)
    {
        delta = default;

        if (original.BsonType == BsonType.Int32 && current.BsonType == BsonType.Int32)
        {
            var diff = current.AsInt32 - original.AsInt32;
            delta = new NumericDelta(diff, BsonType.Int32);
            return true;
        }

        if (original.BsonType == BsonType.Int64 && current.BsonType == BsonType.Int64)
        {
            var diff = current.AsInt64 - original.AsInt64;
            delta = new NumericDelta(diff, BsonType.Int64);
            return true;
        }

        return false;
    }
}
