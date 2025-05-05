using Microsoft.Extensions.Logging;

namespace ExtenDotNet.Tests;

public class Logger<T> : ILogger, ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Console.WriteLine(formatter(state, exception));
}
