using System.Text;
using Microsoft.Extensions.Logging;

namespace App;

/// Lightweight file-based logging for the MAUI desktop app.
///
/// A GUI app has no attached console, so `ILogger` output and — more importantly —
/// unhandled exceptions are otherwise invisible. This writes everything to a plain
/// text file you can `tail -f`, and installs process-wide exception handlers so a
/// crash leaves a trace instead of silently killing the window.
public static class AppLog
{
    private const long _MAX_BYTES = 5 * 1024 * 1024; // rotate once the log passes ~5 MB

    private static readonly object _Gate = new();

    /// Full path of the current log file. Surfaced so the UI (or a menu item) can
    /// reveal it. Nested under a product-named folder (not straight in the shared
    /// app-data / ~/Library area) so the folder we reveal is unmistakably ours and
    /// holds only our logs. Stable across launches.
    public static string LogFilePath { get; } =
        Path.Combine(FileSystem.AppDataDirectory, "DeusaldLocalizer", "Logs", "deusald-localizer.log");

    /// Wire the file logger into the MAUI logging builder.
    public static ILoggingBuilder AddAppFileLog(this ILoggingBuilder builder)
    {
        builder.AddProvider(new FileLoggerProvider());
        return builder;
    }

    /// Install process-wide handlers so nothing an `ILogger` never saw still lands
    /// in the log. Call once, as early as possible during startup.
    public static void HookGlobalExceptions()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogFatal("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        #if WINDOWS
        // WinUI swallows exceptions thrown on the UI thread and tears the app down;
        // handle it so the log is written and the window survives long enough to read it.
        if (Microsoft.UI.Xaml.Application.Current is { } app)
        {
            app.UnhandledException += (_, e) =>
            {
                LogFatal("WinUI.UnhandledException", e.Exception);
                e.Handled = true;
            };
        }
        #endif

        Write("INFO", "AppLog", $"Logging to {LogFilePath}");
    }

    /// Reveal the folder that holds the log file in the OS file manager
    /// (Explorer on Windows, Finder on macOS). Best-effort — never throws.
    public static void OpenLogsFolder()
    {
        try
        {
            string folder = Path.GetDirectoryName(LogFilePath)!;
            Directory.CreateDirectory(folder); // the folder may not exist yet on a first, log-less launch

            #if WINDOWS
            var psi = new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true };
            psi.ArgumentList.Add(folder);
            System.Diagnostics.Process.Start(psi);
            #elif MACCATALYST
            var psi = new System.Diagnostics.ProcessStartInfo { FileName = "open", UseShellExecute = false };
            psi.ArgumentList.Add(folder);
            System.Diagnostics.Process.Start(psi);
            #endif
        }
        catch (Exception ex)
        {
            LogFatal("AppLog.OpenLogsFolder", ex);
        }
    }

    /// Append a single fatal entry (with full exception detail) to the log.
    public static void LogFatal(string source, Exception? ex)
    {
        Write("FATAL", source, ex?.ToString() ?? "<no exception object>");
    }

    /// Append one timestamped line. Thread-safe and best-effort — logging must never
    /// throw and take the app down with it.
    public static void Write(string level, string category, string message)
    {
        try
        {
            lock (_Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                RotateIfNeeded();

                string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] {category}: {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
                System.Diagnostics.Debug.Write(line); // also mirror to the IDE when a debugger is attached
            }
        }
        catch
        {
            // Deliberately swallowed: a logging failure must not crash the app.
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(LogFilePath);
        if (!info.Exists || info.Length < _MAX_BYTES) return;

        string archive = LogFilePath + ".1";
        if (File.Exists(archive)) File.Delete(archive);
        File.Move(LogFilePath, archive);
    }

    private sealed class FileLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName);
        public void Dispose() { }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _Category;

        public FileLogger(string category) => _Category = category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = formatter(state, exception);
            if (exception != null) message += Environment.NewLine + exception;
            Write(logLevel.ToString().ToUpperInvariant(), _Category, message);
        }
    }
}
