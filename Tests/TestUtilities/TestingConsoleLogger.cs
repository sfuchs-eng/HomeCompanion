using Microsoft.Extensions.Logging;

namespace HomeCompanion.Tests.TestUtilities;

public class TestingConsoleLogger : ILogger
{
    private readonly string _categoryName;
    private readonly bool _logToConsole;

    public TestingConsoleLogger(string categoryName, ILoggerProvider? provider, bool logToConsole)
    {
        _categoryName = categoryName;
        _logToConsole = logToConsole;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (_logToConsole)
        {
            Console.Error.WriteLine($"[{logLevel}] {_categoryName}: {formatter(state, exception)}");
        }
    }

    IDisposable? ILogger.BeginScope<TState>(TState state)
    {
        return NullScope.Instance;
    }

    public readonly struct NullScope : IDisposable
    {
        public static readonly NullScope Instance = new NullScope();
        public void Dispose() { }
    }
}
