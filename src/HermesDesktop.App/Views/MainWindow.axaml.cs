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
using HermesDesktop.Core.Rpc;
using HermesDesktop.Platform.Input;

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

        if (ComposerInput is not null)
        {
            ComposerInput.AddHandler(InputElement.KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        }

        if (InlineBesideComposerInput is not null)
        {
            InlineBesideComposerInput.AddHandler(InputElement.KeyDownEvent, OnInlineBesideComposerKeyDown, RoutingStrategies.Tunnel);
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
        var isPlainEnter = e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None;
        var isComboSend = e.Key == Key.Enter
            && (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta);

        if (isPlainEnter || isComboSend)
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

    private void OnInlineBesideComposerKeyDown(object? sender, KeyEventArgs e)
    {
        var isPlainEnter = e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None;
        var isComboSend = e.Key == Key.Enter
            && (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta);

        if ((isPlainEnter || isComboSend)
            && DataContext is MainViewModel vm
            && vm.SendBesideQuickMessageCommand.CanExecute(null))
        {
            e.Handled = true;
            vm.SendBesideQuickMessageCommand.Execute(null);
        }
    }

    private async void OnVoiceDictationClick(object? sender, RoutedEventArgs e)
    {
        if (ComposerInput is not null)
        {
            ComposerInput.Focus();
            ComposerInput.CaretIndex = ComposerInput.Text?.Length ?? 0;
        }

        await Task.Delay(60);

        if (!WindowsInputAndClipboardHelper.TryTriggerSystemVoiceDictation()
            && DataContext is MainViewModel vm)
        {
            vm.ComposerError = LocStrings.Instance.VoiceDictationFallbackNotice;
        }
    }

    private async void OnAttachFilesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;

        IStorageFolder? startFolder = null;
        var lastDir = vm.GetLastAttachmentDirectory();
        if (!string.IsNullOrWhiteSpace(lastDir))
        {
            try
            {
                startFolder = await top.StorageProvider.TryGetFolderFromPathAsync(lastDir);
            }
            catch
            {
                // Fall back to default location if stored path is inaccessible
            }
        }

        var videoFilter = new FilePickerFileType("Videos")
        {
            Patterns = ["*.mp4", "*.mov", "*.m4v", "*.webm", "*.mkv", "*.avi", "*.wmv", "*.flv"]
        };

        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = LocStrings.Instance.AttachFilesTip,
            SuggestedStartLocation = startFolder,
            FileTypeFilter =
            [
                FilePickerFileTypes.All,
                FilePickerFileTypes.ImageAll,
                videoFilter,
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
            var clipText = await top.Clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(clipText)) return;

            var formats = await top.Clipboard.GetFormatsAsync();
            if (formats.Contains(DataFormats.Files))
            {
                if (await top.Clipboard.GetDataAsync(DataFormats.Files) is IEnumerable<IStorageItem> items)
                {
                    var stagedAny = false;
                    foreach (var item in items)
                    {
                        var localPath = item.TryGetLocalPath();
                        if (!string.IsNullOrWhiteSpace(localPath))
                        {
                            vm.StageLocalFile(localPath);
                            stagedAny = true;
                        }
                    }
                    if (stagedAny) return;
                }
            }

            foreach (var fmt in new[] { "PNG", "image/png", "public.png", "DeviceIndependentBitmap", "Format17", "Bitmap" })
            {
                if (!formats.Contains(fmt)) continue;
                var data = await top.Clipboard.GetDataAsync(fmt);
                if (data is Bitmap bmp)
                {
                    using var pngMs = new MemoryStream();
                    bmp.Save(pngMs);
                    vm.StageMemoryAttachment($"pasted-{DateTimeOffset.Now:HHmmss}.png", pngMs.ToArray(), "image/png", AttachmentKind.Image);
                    return;
                }

                byte[]? rawBytes = null;
                if (data is byte[] bytes && bytes.Length > 0)
                {
                    rawBytes = bytes;
                }
                else if (data is Stream s)
                {
                    using var ms = new MemoryStream();
                    await s.CopyToAsync(ms);
                    if (ms.Length > 0) rawBytes = ms.ToArray();
                }

                if (rawBytes is { Length: > 0 })
                {
                    StageNormalizedClipboardImageBytes(vm, rawBytes);
                    return;
                }
            }

            // Win32 native fallback when Avalonia Clipboard misses CF_DIB / CF_DIBV5 screenshots
            var winImage = WindowsInputAndClipboardHelper.TryReadClipboardImageBytes();
            if (winImage is { Bytes.Length: > 0 } img)
            {
                StageNormalizedClipboardImageBytes(vm, img.Bytes);
            }
        }
        catch
        {
            // Ignore transient clipboard lock errors
        }
    }

    private static void StageNormalizedClipboardImageBytes(MainViewModel vm, byte[] rawBytes)
    {
        var normalized = ClipboardImageHelper.WrapRawDibAsBmpIfNeeded(rawBytes);
        try
        {
            using var inMs = new MemoryStream(normalized);
            using var bmp = new Bitmap(inMs);
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs);
            vm.StageMemoryAttachment($"screenshot-{DateTimeOffset.Now:HHmmss}.png", pngMs.ToArray(), "image/png", AttachmentKind.Image);
        }
        catch
        {
            var isPng = ClipboardImageHelper.HasEncodedImageSignature(normalized)
                && normalized.Length >= 4 && normalized[0] == 0x89;
            var ext = isPng ? "png" : "bmp";
            var mime = isPng ? "image/png" : "image/bmp";
            vm.StageMemoryAttachment($"screenshot-{DateTimeOffset.Now:HHmmss}.{ext}", normalized, mime, AttachmentKind.Image);
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
