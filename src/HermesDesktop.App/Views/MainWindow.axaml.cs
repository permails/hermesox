using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HermesDesktop.App.Controls;
using HermesDesktop.App.Localization;
using HermesDesktop.App.ViewModels;
using HermesDesktop.Core.Models;

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

        if (ComposerDropTarget is not null)
        {
            ComposerDropTarget.AddHandler(DragDrop.DragOverEvent, OnComposerDragOver);
            ComposerDropTarget.AddHandler(DragDrop.DropEvent, OnComposerDrop);
        }
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
            return;
        }

        if (e.Key == Key.V && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            _ = TryPasteAttachmentFromClipboardAsync();
        }
    }

    private async void OnAttachFilesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;

        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = LocStrings.Instance.AttachFilesTip,
            FileTypeFilter =
            [
                FilePickerFileTypes.All,
                FilePickerFileTypes.ImageAll,
                FilePickerFileTypes.TextPlain
            ]
        });

        foreach (var file in picked)
        {
            var localPath = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                vm.StageLocalFile(localPath);
            }
            else
            {
                await using var stream = await file.OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                vm.StageMemoryAttachment(file.Name, ms.ToArray());
            }
        }
    }

    private void OnComposerDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnComposerDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var files = e.Data.GetFiles();
        if (files is null) return;

        foreach (var item in files)
        {
            var localPath = item.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                vm.StageLocalFile(localPath);
            }
        }
        e.Handled = true;
    }

    private async Task TryPasteAttachmentFromClipboardAsync()
    {
        if (DataContext is not MainViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is null) return;

        try
        {
            var formats = await top.Clipboard.GetFormatsAsync();
            if (formats.Contains(DataFormats.Text)) return;

            if (formats.Contains(DataFormats.Files))
            {
                if (await top.Clipboard.GetDataAsync(DataFormats.Files) is IEnumerable<IStorageItem> items)
                {
                    foreach (var item in items)
                    {
                        var localPath = item.TryGetLocalPath();
                        if (!string.IsNullOrWhiteSpace(localPath))
                        {
                            vm.StageLocalFile(localPath);
                        }
                    }
                    return;
                }
            }

            foreach (var fmt in new[] { "PNG", "image/png", "public.png", "Bitmap" })
            {
                if (!formats.Contains(fmt)) continue;
                var data = await top.Clipboard.GetDataAsync(fmt);
                if (data is byte[] bytes && bytes.Length > 0)
                {
                    vm.StageMemoryAttachment($"pasted-{DateTimeOffset.Now:HHmmss}.png", bytes, "image/png", AttachmentKind.Image);
                    return;
                }
                if (data is Stream s)
                {
                    using var ms = new MemoryStream();
                    await s.CopyToAsync(ms);
                    if (ms.Length > 0)
                    {
                        vm.StageMemoryAttachment($"pasted-{DateTimeOffset.Now:HHmmss}.png", ms.ToArray(), "image/png", AttachmentKind.Image);
                        return;
                    }
                }
                if (data is Bitmap bmp)
                {
                    using var ms = new MemoryStream();
                    bmp.Save(ms);
                    vm.StageMemoryAttachment($"pasted-{DateTimeOffset.Now:HHmmss}.png", ms.ToArray(), "image/png", AttachmentKind.Image);
                    return;
                }
            }
        }
        catch
        {
            // Ignore transient clipboard lock errors
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

    private async void OnCopyPreviewSelectionClick(object? sender, RoutedEventArgs e)
    {
        if (PreviewTextBox is null) return;
        var selected = PreviewTextBox.SelectedText;
        var text = string.IsNullOrEmpty(selected) ? (PreviewTextBox.Text ?? "") : selected;
        await CopyPreviewTextWithFeedbackAsync(text, CopyPreviewSelectionBtn, () => LocStrings.Instance.CopySelectionBtn);
    }

    private async void OnCopyPreviewAllClick(object? sender, RoutedEventArgs e)
    {
        if (PreviewTextBox is null) return;
        await CopyPreviewTextWithFeedbackAsync(PreviewTextBox.Text ?? "", CopyPreviewAllBtn, () => LocStrings.Instance.CopyAllBtn);
    }

    private async Task CopyPreviewTextWithFeedbackAsync(string text, Button? button, Func<string> defaultLabel)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is not null)
            {
                await top.Clipboard.SetTextAsync(text);
                if (button is not null)
                {
                    button.Content = LocStrings.Instance.CopiedBtn;
                    await Task.Delay(1500);
                    button.Content = defaultLabel();
                }
            }
        }
        catch
        {
            // Clipboard access may fail transiently on locked desktops
        }
    }
}
