using Avalonia;
using Avalonia.WebView.Desktop;
using HermesDesktop.Core.Diagnostics;

namespace HermesDesktop.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDiagnostics.InstallGlobalHandlers();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogCrash("PROGRAM_MAIN", ex);
            throw;
        }
        finally
        {
            AppDiagnostics.Log("APP", "HermesDesktop exiting cleanly.");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .UseDesktopWebView()
            .LogToTrace();
}
