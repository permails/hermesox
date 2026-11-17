using System.Diagnostics;
using HermesDesktop.Core.Storage;

namespace HermesDesktop.Core.Diagnostics;

/// <summary>
/// Lightweight diagnostics logger & UI-thread stall watchdog.
/// Records the current active phase and writes stall reports to diagnostics.log
/// whenever the UI thread stops responding for more than 1000 ms.
/// </summary>
public static class AppDiagnostics
{
    private static readonly object LogLock = new();
    private static volatile string _currentPhase = "startup";
    private static volatile string _lastEventDetail = "";
    private static long _lastUiHeartbeatTicks = Stopwatch.GetTimestamp();
    private static int _watchdogStarted;

    public static string LogFilePath => Path.Combine(AppDataPaths.GetDataDirectory(), "diagnostics.log");

    public static void SetPhase(string phase, string? detail = null)
    {
        _currentPhase = phase;
        if (detail is not null)
        {
            _lastEventDetail = detail.Length > 160 ? detail[..160] + "…" : detail;
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
            var path = LogFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}{Environment.NewLine}";
            lock (LogLock)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                {
                    File.WriteAllText(path, string.Empty);
                }
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Never crash on diagnostics logging
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
