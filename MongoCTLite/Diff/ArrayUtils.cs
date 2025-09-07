using MongoDB.Bson;
using System.Linq;

namespace MongoCTLite.Diff;

public static class ArrayUtils
{
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
        
        return current.All(item => original.Contains(item));
    }
    
    /// <summary>
    /// Calculates the array change ratio (0.0 ~ 1.0)
    /// </summary>
    public static double CalculateChangeRatio(BsonArray original, BsonArray current)
    {
        if (original.Count == 0)
            return current.Count == 0 ? 0.0 : 1.0;
        
        var sizeChange = Math.Abs(current.Count - original.Count);
        var maxSize = Math.Max(original.Count, current.Count);
        
        // Calculate common elements
        var commonCount = 0;
        var minCount = Math.Min(original.Count, current.Count);
        
        for (int i = 0; i < minCount; i++)
        {
            if (BsonUtils.Equals(original[i], current[i]))
                commonCount++;
        }
        
        var changedElements = minCount - commonCount + sizeChange;
        return (double)changedElements / maxSize;
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
        if (!IsRemoveOnly(original, current))
            return new List<BsonValue>();
            
        return original.Where(x => !current.Contains(x)).ToList();
    }
}