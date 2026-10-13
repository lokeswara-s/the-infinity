using Microsoft.Extensions.Logging;

namespace TheInfinity.App.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileLoggerProvider(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _path, _gate);

    public void Dispose()
    {
    }

    private sealed class FileLogger(string category, string path, object gate) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            var line = $"{DateTimeOffset.Now:O} {logLevel} {category}: {formatter(state, exception)}";
            if (exception is not null)
                line += Environment.NewLine + exception;
            lock (gate)
            {
                try
                {
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch
                {
                    // Logging must not take down window management.
                }
            }
        }
    }
}
