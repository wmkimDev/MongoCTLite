using MongoCTLite.Abstractions;

namespace MongoCTLite.Tests;

public sealed class NoopLogger : IRunLogger
{
    public void Info(string msg) { }
    public void Warn(string msg) { }
    public void Error(Exception ex, string msg) { }
}