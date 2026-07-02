using Microsoft.Extensions.Logging;

namespace HomeCompanion.Tests.TestUtilities;

public class ConsoleLoggerFactory : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider) { }
    public ILogger CreateLogger(string categoryName) => new TestingConsoleLogger(categoryName, null, true);
    public void Dispose() { }
}
