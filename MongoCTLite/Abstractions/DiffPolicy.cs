namespace MongoCTLite.Abstractions;

public sealed record DiffPolicy(
    int MaxFieldOpsBeforeDocReplace = 32,     // If the number of changed fields exceeds this, replace the entire document
    double ArrayChangeRatioForReplace = 0.35, // If the array change rate exceeds the threshold, replace the entire array
    Func<string, bool>? AllowIncPath = null,  // Whether the field can be converted to $inc for numeric increment
    Func<string, bool>? AllowUnsetPath = null // Whether null can be converted to $unset for the field
)
{
    public bool AllowInc(string path) => AllowIncPath?.Invoke(path) ?? false;
    public bool AllowUnset(string path) => AllowUnsetPath?.Invoke(path) ?? true;
}

