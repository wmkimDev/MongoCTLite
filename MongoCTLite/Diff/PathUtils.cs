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
            
        return $"{prefix}.{key}";
    }
}