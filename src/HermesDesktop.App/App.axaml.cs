using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HermesDesktop.App.ViewModels;
using HermesDesktop.App.Views;
using HermesDesktop.Core.Diagnostics;
using HermesDesktop.Core.Storage;
using HermesDesktop.Platform.Notifications;
using HermesDesktop.Platform.Security;

namespace HermesDesktop.App;

public partial class App : Application
{
    private CancellationTokenSource? _watchdogCts;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _watchdogCts = new CancellationTokenSource();
            AppDiagnostics.StartUiWatchdog(
                action => Dispatcher.UIThread.Post(action, DispatcherPriority.Send),
                _watchdogCts.Token);

            var cache = new LocalCacheStore();
            var vault = new DpapiCredentialVault();
            var notifications = new DesktopNotificationService();
            var vm = new MainViewModel(cache, vault, notifications);

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                AppDiagnostics.LogCrash("UI_DISPATCHER_UNHANDLED", e.Exception);
                if (e.Exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
                {
                    e.Handled = true;
                    vm.StatusBanner = $"Unexpected UI error recorded to log: {e.Exception.Message}";
                }
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.ShutdownRequested += async (_, _) =>
            {
                _watchdogCts?.Cancel();
                await vm.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
