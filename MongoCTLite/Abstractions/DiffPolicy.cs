namespace MongoCTLite.Abstractions;

public sealed record DiffPolicy(
    int MaxFieldOpsBeforeDocReplace = 32,    // If the number of changed fields exceeds this, replace the entire document
    Func<string, bool>? AllowIncPath = null, // Whether the field can be converted to $inc for numeric increment
    Func<string, bool>? AllowUnsetPath = null // Whether null can be converted to $unset for the field
)
{
    public bool AllowInc(string path) => AllowIncPath?.Invoke(path) ?? false;
    public bool AllowUnset(string path) => AllowUnsetPath?.Invoke(path) ?? true;

    public static DiffPolicy WithInc(params string[] paths)
        => new DiffPolicy(AllowIncPath: BuildPathMatcher(paths));

    public static DiffPolicy WithInc(IEnumerable<string> paths)
        => new DiffPolicy(AllowIncPath: BuildPathMatcher(paths));

    public DiffPolicy WithAdditionalInc(params string[] paths)
        => this with { AllowIncPath = Combine(AllowIncPath, BuildPathMatcher(paths)) };

    public DiffPolicy WithAdditionalInc(IEnumerable<string> paths)
        => this with { AllowIncPath = Combine(AllowIncPath, BuildPathMatcher(paths)) };

    private static Func<string, bool> BuildPathMatcher(IEnumerable<string> paths)
    {
        var set = new HashSet<string>(paths ?? Array.Empty<string>(), StringComparer.Ordinal);
        return path => set.Contains(path);
    }

    private static Func<string, bool>? Combine(Func<string, bool>? existing, Func<string, bool> next)
        => existing is null ? next : path => existing(path) || next(path);
}

