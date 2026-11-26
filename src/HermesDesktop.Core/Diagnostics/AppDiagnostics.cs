using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HermesDesktop.Core.Storage;

namespace HermesDesktop.Core.Diagnostics;

/// <summary>
/// Thread-safe local logging, breadcrumb ring buffer, global exception / crash capture,
/// and UI-thread stall watchdog writing daily rotated log files under the local data directory.
/// </summary>
public static class AppDiagnostics
{
    private static readonly object LogLock = new();
    private static readonly Queue<string> RecentBreadcrumbs = new(48);
    private static volatile string _currentPhase = "startup";
    private static volatile string _lastEventDetail = "";
    private static long _lastUiHeartbeatTicks = Stopwatch.GetTimestamp();
    private static int _watchdogStarted;
    private static int _handlersInstalled;

    private const long MaxSingleLogBytes = 10 * 1024 * 1024;
    private const int RetentionDays = 14;

    public static string LogsDirectory => AppDataPaths.LogsDirectory;

    public static string LogFilePath => Path.Combine(
        LogsDirectory,
        $"hermes-{DateTimeOffset.Now:yyyy-MM-dd}.log");

    public static string CrashLogFilePath => Path.Combine(
        LogsDirectory,
        $"crash-{DateTimeOffset.Now:yyyy-MM-dd}.log");

    public static void InstallGlobalHandlers()
    {
        if (Interlocked.Exchange(ref _handlersInstalled, 1) != 0) return;

        CleanupOldLogs();
        Log("APP", $"HermesDesktop started. OS={RuntimeInformation.OSDescription}, Arch={RuntimeInformation.ProcessArchitecture}, Runtime={RuntimeInformation.FrameworkDescription}, DataDir={AppDataPaths.GetDataDirectory()}, LogsDir={LogsDirectory}");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            var msg = ex is null ? (e.ExceptionObject?.ToString() ?? "Unknown non-Exception fatal object") : null;
            LogCrash("APPDOMAIN_UNHANDLED", ex, $"IsTerminating={e.IsTerminating}; Object={msg}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogError("UNOBSERVED_TASK", "Unobserved background task exception caught", e.Exception);
            e.SetObserved();
        };
    }

    public static void SetPhase(string phase, string? detail = null)
    {
        _currentPhase = phase;
        if (detail is not null)
        {
            _lastEventDetail = detail.Length > 200 ? detail[..200] + "…" : detail;
        }
    }

    public static void BeatUi()
    {
        Interlocked.Exchange(ref _lastUiHeartbeatTicks, Stopwatch.GetTimestamp());
    }

    public static void Log(string category, string message)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var line = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}{Environment.NewLine}";
            WriteLineToDailyLog(line, now);
        }
        catch
        {
            // Never crash on diagnostics logging
        }
    }

    public static void LogError(string category, string context, Exception? ex)
    {
        if (ex is null)
        {
            Log(category, context);
            return;
        }
        Log(category, $"{context} | {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex}");
    }

    public static void LogCrash(string source, Exception? ex, string? extraInfo = null)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var sb = new StringBuilder();
            sb.AppendLine($"================ CRASH REPORT [{now:yyyy-MM-dd HH:mm:ss.fff}] ================");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"Phase: {_currentPhase}");
            sb.AppendLine($"LastDetail: {_lastEventDetail}");
            if (!string.IsNullOrWhiteSpace(extraInfo)) sb.AppendLine($"Extra: {extraInfo}");
            sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
            if (ex is not null)
            {
                sb.AppendLine("Exception:");
                sb.AppendLine(ex.ToString());
            }
            lock (LogLock)
            {
                if (RecentBreadcrumbs.Count > 0)
                {
                    sb.AppendLine("Recent Breadcrumbs:");
                    foreach (var item in RecentBreadcrumbs)
                    {
                        sb.Append("  ").Append(item);
                    }
                }
                sb.AppendLine("===========================================================================");
                var report = sb.ToString();
                var daily = Path.Combine(LogsDirectory, $"hermes-{now:yyyy-MM-dd}.log");
                var crash = Path.Combine(LogsDirectory, $"crash-{now:yyyy-MM-dd}.log");
                File.AppendAllText(daily, report, Encoding.UTF8);
                File.AppendAllText(crash, report, Encoding.UTF8);
            }
        }
        catch
        {
            // Never throw from crash logger
        }
    }

    private static void WriteLineToDailyLog(string line, DateTimeOffset now)
    {
        var path = Path.Combine(LogsDirectory, $"hermes-{now:yyyy-MM-dd}.log");
        lock (LogLock)
        {
            if (RecentBreadcrumbs.Count >= 40) RecentBreadcrumbs.Dequeue();
            RecentBreadcrumbs.Enqueue(line);

            if (File.Exists(path) && new FileInfo(path).Length > MaxSingleLogBytes)
            {
                var rotated = Path.Combine(
                    LogsDirectory,
                    $"hermes-{now:yyyy-MM-dd}-{now:HHmmss}.log");
                File.Move(path, rotated, overwrite: true);
            }
            File.AppendAllText(path, line, Encoding.UTF8);
        }
    }

    private static void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTimeOffset.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.GetFiles(LogsDirectory, "*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Ignore cleanup failure
        }
    }

    public static void StartUiWatchdog(Action<Action> dispatchToUi, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _watchdogStarted, 1) != 0) return;

        Log("WATCHDOG", "UI freeze watchdog started.");
        _ = Task.Run(async () =>
        {
            var stallReported = false;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(500, ct);
                    dispatchToUi(BeatUi);

                    var last = Interlocked.Read(ref _lastUiHeartbeatTicks);
                    var elapsedMs = (Stopwatch.GetTimestamp() - last) * 1000.0 / Stopwatch.Frequency;
                    if (elapsedMs > 1200)
                    {
                        if (!stallReported)
                        {
                            stallReported = true;
                            Log("UI_STALL", $"UI thread unresponsive for {elapsedMs:0}ms! Phase={_currentPhase}, Detail={_lastEventDetail}");
                        }
                    }
                    else if (stallReported)
                    {
                        stallReported = false;
                        Log("UI_RECOVERED", $"UI thread recovered after stall. Phase={_currentPhase}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Keep watchdog alive
                }
            }
        }, ct);
    }
}
