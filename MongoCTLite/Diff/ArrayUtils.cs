using System.Collections.Generic;
using MongoDB.Bson;
using System.Linq;

namespace MongoCTLite.Diff;

public static class ArrayUtils
{
    private static readonly IEqualityComparer<BsonValue> ValueComparer = new BsonValueComparer();

    /// <summary>
    /// Checks if the change is append-only (added only at the end)
    /// </summary>
    public static bool IsAppendOnly(BsonArray original, BsonArray current)
    {
        if (current.Count < original.Count)
            return false;
        
        for (int i = 0; i < original.Count; i++)
        {
            if (!BsonUtils.Equals(original[i], current[i]))
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Checks if the change is remove-only (only some items removed)
    /// </summary>
    public static bool IsRemoveOnly(BsonArray original, BsonArray current)
    {
        if (current.Count > original.Count)
            return false;

        var originalCounts = new Dictionary<BsonValue, int>(original.Count, ValueComparer);
        foreach (var item in original)
            originalCounts[item] = originalCounts.TryGetValue(item, out var n) ? n + 1 : 1;

        var currentCounts = new Dictionary<BsonValue, int>(current.Count, ValueComparer);
        foreach (var item in current)
        {
            if (!originalCounts.TryGetValue(item, out var originalCount))
                return false;

            var next = currentCounts.TryGetValue(item, out var existing) ? existing + 1 : 1;
            if (next > originalCount)
                return false;

            currentCounts[item] = next;
        }

        var removed = false;
        foreach (var (value, originalCount) in originalCounts)
        {
            currentCounts.TryGetValue(value, out var currentCount);
            if (currentCount == originalCount)
                continue;

            if (currentCount == 0)
            {
                removed = true;
                continue;
            }

            return false; // Partial removal of duplicates → fallback to replace
        }

        return removed;
    }
    
    /// <summary>
    /// Extracts appended elements
    /// </summary>
    public static List<BsonValue> GetAppendedElements(BsonArray original, BsonArray current)
    {
        if (!IsAppendOnly(original, current))
            return new List<BsonValue>();
            
        return current.Skip(original.Count).ToList();
    }
    
    /// <summary>
    /// Extracts removed elements
    /// </summary>
    public static List<BsonValue> GetRemovedElements(BsonArray original, BsonArray current)
    {
        var originalCounts = new Dictionary<BsonValue, int>(original.Count, ValueComparer);
        foreach (var item in original)
            originalCounts[item] = originalCounts.TryGetValue(item, out var n) ? n + 1 : 1;

        var currentCounts = new Dictionary<BsonValue, int>(current.Count, ValueComparer);
        foreach (var item in current)
            currentCounts[item] = currentCounts.TryGetValue(item, out var n) ? n + 1 : 1;

        var removed = new List<BsonValue>();
        foreach (var (value, originalCount) in originalCounts)
        {
            currentCounts.TryGetValue(value, out var currentCount);
            if (currentCount == originalCount)
                continue;

            // currentCount is necessarily 0 here thanks to IsRemoveOnly
            removed.Add(value);
        }

        return removed;
    }

    private sealed class BsonValueComparer : IEqualityComparer<BsonValue>
    {
        public bool Equals(BsonValue? x, BsonValue? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return BsonUtils.Equals(x, y);
        }

        public int GetHashCode(BsonValue obj) => obj.GetHashCode();
    }
}
