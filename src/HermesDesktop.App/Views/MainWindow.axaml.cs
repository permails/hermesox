using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HermesDesktop.App.Controls;
using HermesDesktop.App.ViewModels;

namespace HermesDesktop.App.Views;

public partial class MainWindow : Window
{
    private readonly TranscriptAutoScrollController? _transcriptAutoScroll;

    public MainWindow()
    {
        InitializeComponent();

        if (TranscriptScroller is not null)
        {
            _transcriptAutoScroll = new TranscriptAutoScrollController(TranscriptScroller);
            _transcriptAutoScroll.StickyChanged += sticky =>
            {
                if (JumpToLatestButton is not null)
                {
                    JumpToLatestButton.IsVisible = !sticky;
                }
            };
        }

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.TranscriptScrollRequested += force => _transcriptAutoScroll?.RequestScrollToBottom(force);
                vm.HtmlPreviewContentLoaded += html => LoadPreviewHtml(html, vm.HtmlPreviewZoom);
                vm.HtmlPreviewZoomChanged += zoom => _ = ApplyPreviewZoomAsync(zoom);
            }
        };
    }

    private void LoadPreviewHtml(string html, double zoom)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (InlinePreviewWebView is null) return;
            var zStr = zoom.ToString("0.00", CultureInfo.InvariantCulture);
            InlinePreviewWebView.HtmlContent = InjectInitialZoomStyle(html, zStr);
        });
    }

    private async Task ApplyPreviewZoomAsync(double zoom)
    {
        if (InlinePreviewWebView is null) return;
        try
        {
            var zStr = zoom.ToString("0.00", CultureInfo.InvariantCulture);
            await InlinePreviewWebView.ExecuteScriptAsync(
                $"document.documentElement.style.zoom='{zStr}';window.dispatchEvent(new Event('resize'));");
        }
        catch
        {
            // Ignore if WebView is still initializing
        }
    }

    private static string InjectInitialZoomStyle(string html, string zoom)
    {
        var styleTag = $"<style id=\"__hermes_zoom_style\">html {{ zoom: {zoom}; }}</style>";
        var headClose = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return headClose >= 0 ? html.Insert(headClose, styleTag) : styleTag + html;
    }

    private void OnJumpToLatestClick(object? sender, RoutedEventArgs e)
    {
        _transcriptAutoScroll?.RequestScrollToBottom(force: true);
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            if (DataContext is MainViewModel vm && vm.SendOrQueueCommand.CanExecute(null))
            {
                vm.SendOrQueueCommand.Execute(null);
            }
        }
    }

    private void OnPopOutBesideClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsBesideVisible) return;
        var win = new BesideChatWindow
        {
            DataContext = vm
        };
        win.Show();
    }
}
