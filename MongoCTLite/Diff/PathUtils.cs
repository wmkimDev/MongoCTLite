namespace MongoCTLite.Diff;

public static class PathUtils
{
    /// <summary>
    /// Combine MongoDB field paths
    /// </summary>
    public static string Join(string prefix, string key)
    {
        if (string.IsNullOrEmpty(prefix))
            return key;
        
        // Array indices are concatenated directly
        if (key.StartsWith("["))
            return $"{prefix}{key}";
            
        return $"{prefix}.{key}";
    }
}