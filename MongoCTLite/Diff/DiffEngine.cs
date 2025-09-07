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
    private static readonly HashSet<string> ReservedRootKeys =
        new(StringComparer.Ordinal) { "_id", "version" };
    
    public static WriteModel<BsonDocument>? BuildModel<T>(
        TrackingEntry<T> e, 
        DiffPolicy policy)
    {
        var original = e.Original;
        var current = e.Current!.ToBsonDocument();
        
        if (current.TryGetValue(e.IdField, out var incomingId) && incomingId != e.Id)
            throw new InvalidOperationException($"`{e.IdField}` field cannot be modified.");
        
        if (!current.Contains("_id"))
            current["_id"] = e.Id;
        
        var ops = ComputeDiff(original, current, policy);
        
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
        DiffPolicy policy)
    {
        var ops = new UpdateOps();
        DiffDocument("", original, current, ops, policy);
        return ops;
    }
    
    private static void DiffDocument(
        string prefix,
        BsonDocument original,
        BsonDocument current,
        UpdateOps ops,
        DiffPolicy policy)
    {
        var originalKeys = original.Names.ToHashSet();
        var currentKeys = current.Names.ToHashSet();
        
        if (prefix.Length == 0)
        {
            originalKeys.ExceptWith(ReservedRootKeys);
            currentKeys.ExceptWith(ReservedRootKeys);
        }
        
        // 삭제된 필드
        foreach (var key in originalKeys.Except(currentKeys))
        {
            var path = Join(prefix, key);
            
            if (policy.AllowUnset(path))
                ops.Unsets.Add(path);
            else
                ops.Sets[path] = BsonNull.Value;
        }
        
        // 현재 필드
        foreach (var key in currentKeys)
        {
            var path = Join(prefix, key);
            var currentValue = current[key];
            
            if (!original.TryGetValue(key, out var originalValue))
            {
                ops.Sets[path] = currentValue;
                continue;
            }
            
            DiffValue(path, originalValue, currentValue, ops, policy);
        }
    }

    private static void DiffValue(
        string path,
        BsonValue original,
        BsonValue current,
        UpdateOps ops,
        DiffPolicy policy)
    {
        if (Equals(original, current))
            return;
        
        switch ((original.BsonType, current.BsonType))
        {
            case (BsonType.Document, BsonType.Document):
                DiffDocument(path, original.AsBsonDocument, current.AsBsonDocument, ops, policy);
                break;
            
            case (BsonType.Array, BsonType.Array):
                DiffArray(path, original.AsBsonArray, current.AsBsonArray, ops, policy);
                break;
            
            case var types when AreBothNumeric(original, current):
                if (policy.AllowInc(path))
                {
                    var delta = ToInt64Safe(current) - ToInt64Safe(original);
                    if (delta != 0)
                    {
                        ops.Incs[path] = ops.Incs.TryGetValue(path, out var existing) 
                            ? existing + delta 
                            : delta;
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
        UpdateOps ops,
        DiffPolicy policy)
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
        
        // 변화율 체크
        var changeRatio = CalculateChangeRatio(original, current);
        if (changeRatio >= policy.ArrayChangeRatioForReplace)
        {
            ops.Sets[path] = current;
        }
        else
        {
            ops.Sets[path] = current;
        }
    }
    
    private static WriteModel<BsonDocument> CreateReplaceModel<T>(
        TrackingEntry<T> e, BsonDocument current)
    {
        var f = Builders<BsonDocument>.Filter;

        if (current.TryGetValue(e.IdField, out var incomingId) && incomingId != e.Id)
            throw new InvalidOperationException($"`{e.IdField}` field cannot be modified.");

        current[e.IdField] = e.Id;
        current["version"] = e.ExpectedVersion!.Value + 1;
        var filter = f.Eq(e.IdField, e.Id) & f.Eq("version", e.ExpectedVersion!.Value);

        return new ReplaceOneModel<BsonDocument>(filter, current)
        {
            IsUpsert = false
        };
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
            updates.Add(Builders<BsonDocument>.Update.Inc(path, delta));

        foreach (var (path, values) in ops.Pushes)
            updates.Add(values.Count == 1
                ? Builders<BsonDocument>.Update.Push(path, values[0])
                : Builders<BsonDocument>.Update.PushEach(path, values));

        foreach (var (path, values) in ops.Pulls)
            updates.Add(values.Count == 1
                ? Builders<BsonDocument>.Update.Pull(path, values[0])
                : Builders<BsonDocument>.Update.PullAll(path, values));

        updates.Add(Builders<BsonDocument>.Update.Inc("version", 1));

        var f      = Builders<BsonDocument>.Filter;
        var filter = f.Eq(e.IdField, e.Id) & f.Eq("version", e.ExpectedVersion!.Value);

        return new UpdateOneModel<BsonDocument>(filter, Builders<BsonDocument>.Update.Combine(updates))
        {
            IsUpsert = false
        };
    }
}