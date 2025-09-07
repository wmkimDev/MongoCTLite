namespace MongoCTLite.Abstractions;

public interface IRunLogger
{
    void Info(string msg);
    void Warn(string msg);
    void Error(Exception ex, string msg);
}