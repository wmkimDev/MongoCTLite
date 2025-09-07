namespace MongoCTLite.Diff;

public static class PathUtils
{
    /// <summary>
    /// MongoDB 필드 경로 조합
    /// </summary>
    public static string Join(string prefix, string key)
    {
        if (string.IsNullOrEmpty(prefix))
            return key;
        
        // 배열 인덱스는 바로 붙임
        if (key.StartsWith("["))
            return $"{prefix}{key}";
            
        return $"{prefix}.{key}";
    }
}