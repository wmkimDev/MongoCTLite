using MongoCTLite.Abstractions;
namespace MongoCTLite.Infrastructure;

public sealed class ConsoleRunLogger : IRunLogger
{
    public void Info(string msg)  => Console.WriteLine($"[info] {msg}");
    public void Warn(string msg)  => Console.WriteLine($"[warn] {msg}");
    public void Error(Exception ex, string msg)
        => Console.WriteLine($"[error] {msg} :: {ex.GetType().Name}: {ex.Message}");
}