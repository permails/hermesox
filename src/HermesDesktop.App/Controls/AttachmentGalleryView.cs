using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Models;

namespace HermesDesktop.App.Controls;

public sealed class AttachmentGalleryView : UserControl
{
    public static readonly StyledProperty<ObservableCollection<Attachment>?> AttachmentsProperty =
        AvaloniaProperty.Register<AttachmentGalleryView, ObservableCollection<Attachment>?>(nameof(Attachments));

    public static readonly StyledProperty<Func<string, Task<byte[]?>>?> MediaBytesLoaderProperty =
        AvaloniaProperty.Register<AttachmentGalleryView, Func<string, Task<byte[]?>>?>(nameof(MediaBytesLoader));

    public static readonly StyledProperty<ICommand?> ImageClickCommandProperty =
        AvaloniaProperty.Register<AttachmentGalleryView, ICommand?>(nameof(ImageClickCommand));

    public static readonly StyledProperty<ICommand?> FileClickCommandProperty =
        AvaloniaProperty.Register<AttachmentGalleryView, ICommand?>(nameof(FileClickCommand));

    public static readonly StyledProperty<ICommand?> DownloadCommandProperty =
        AvaloniaProperty.Register<AttachmentGalleryView, ICommand?>(nameof(DownloadCommand));

    public static readonly StyledProperty<bool> CompactThumbnailsProperty =
        AvaloniaProperty.Register<AttachmentGalleryView, bool>(nameof(CompactThumbnails));

    public ObservableCollection<Attachment>? Attachments
    {
        get => GetValue(AttachmentsProperty);
        set => SetValue(AttachmentsProperty, value);
    }

    public Func<string, Task<byte[]?>>? MediaBytesLoader
    {
        get => GetValue(MediaBytesLoaderProperty);
        set => SetValue(MediaBytesLoaderProperty, value);
    }

    public ICommand? ImageClickCommand
    {
        get => GetValue(ImageClickCommandProperty);
        set => SetValue(ImageClickCommandProperty, value);
    }

    public ICommand? FileClickCommand
    {
        get => GetValue(FileClickCommandProperty);
        set => SetValue(FileClickCommandProperty, value);
    }

    public ICommand? DownloadCommand
    {
        get => GetValue(DownloadCommandProperty);
        set => SetValue(DownloadCommandProperty, value);
    }

    public bool CompactThumbnails
    {
        get => GetValue(CompactThumbnailsProperty);
        set => SetValue(CompactThumbnailsProperty, value);
    }

    private readonly WrapPanel _root = new() { Orientation = Orientation.Horizontal };

    public AttachmentGalleryView()
    {
        Content = _root;
        ActualThemeVariantChanged += (_, _) => Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AttachmentsProperty
            || change.Property == MediaBytesLoaderProperty
            || change.Property == CompactThumbnailsProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _root.Children.Clear();
        var items = Attachments;
        if (items is null || items.Count == 0) return;

        var imageCount = items.Count(a => a.Kind == AttachmentKind.Image);
        var useCompactImages = CompactThumbnails || imageCount > 1;

        foreach (var att in items)
        {
            if (att.Kind == AttachmentKind.Image)
            {
                _root.Children.Add(BuildImageTile(att, useCompactImages));
            }
            else
            {
                _root.Children.Add(BuildFileTile(att));
            }
        }
    }

    private Control BuildImageTile(Attachment att, bool compact)
    {
        var placeholder = new Border
        {
            MinWidth = compact ? 68 : 180,
            MinHeight = compact ? 68 : 110,
            MaxWidth = compact ? 68 : 360,
            MaxHeight = compact ? 68 : 260,
            Width = compact ? 68 : double.NaN,
            Height = compact ? 68 : double.NaN,
            CornerRadius = new CornerRadius(compact ? 6 : 4),
            ClipToBounds = true,
            Background = FluentPalette.CardSecondary,
            BorderBrush = FluentPalette.CardStroke,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, compact ? 6 : 8, 6),
            Child = new TextBlock
            {
                Text = compact ? "🖼" : AppLocalization.T($"Loading {att.Name}...", $"正在加载 {att.Name}..."),
                FontSize = compact ? 18 : 11.5,
                Foreground = FluentPalette.TextMuted,
                Margin = new Thickness(compact ? 4 : 14, compact ? 4 : 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        if (compact) ToolTip.SetTip(placeholder, att.Name);

        var loader = MediaBytesLoader;
        if (loader is not null)
        {
            _ = Task.Run(async () =>
            {
                var bytes = await loader(att.Uri);
                Dispatcher.UIThread.Post(() =>
                {
                    if (bytes is { Length: > 0 })
                    {
                        try
                        {
                            using var ms = new MemoryStream(bytes);
                            var bmp = new Bitmap(ms);
                            var img = compact
                                ? new Image
                                {
                                    Source = bmp,
                                    Width = 68,
                                    Height = 68,
                                    Stretch = Stretch.UniformToFill
                                }
                                : new Image
                                {
                                    Source = bmp,
                                    MaxWidth = 360,
                                    MaxHeight = 260,
                                    Stretch = Stretch.Uniform
                                };
                            var btn = new Button
                            {
                                Padding = new Thickness(0),
                                Background = Brushes.Transparent,
                                BorderThickness = new Thickness(0),
                                CornerRadius = new CornerRadius(compact ? 6 : 4),
                                Content = img
                            };
                            ToolTip.SetTip(btn, AppLocalization.T($"{att.Name} — Click to enlarge", $"{att.Name} — 点击查看大图"));
                            btn.Click += (_, _) =>
                            {
                                if (ImageClickCommand?.CanExecute(att.Uri) == true)
                                {
                                    ImageClickCommand.Execute(att.Uri);
                                }
                            };
                            placeholder.MinWidth = 0;
                            placeholder.MinHeight = 0;
                            placeholder.Child = btn;
                            return;
                        }
                        catch
                        {
                            // Fallback to error text below
                        }
                    }

                    placeholder.MinHeight = compact ? 68 : 48;
                    placeholder.Child = new TextBlock
                    {
                        Text = compact ? "⚠" : AppLocalization.T($"Image unavailable: {att.Name}", $"无法加载图片: {att.Name}"),
                        FontSize = 11.5,
                        Foreground = FluentPalette.TextMuted,
                        Margin = new Thickness(compact ? 6 : 12, 8),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                });
            });
        }

        return placeholder;
    }

    private Control BuildFileTile(Attachment att)
    {
        var icon = att.Kind == AttachmentKind.Video ? "🎬" : "📎";
        var openBtn = new Button
        {
            Content = $"{icon} {att.Name}",
            FontSize = 12,
            Padding = new Thickness(10, 5),
            CornerRadius = new CornerRadius(4),
            Background = FluentPalette.ControlFill,
            BorderBrush = FluentPalette.ControlStroke,
            BorderThickness = new Thickness(1),
            Foreground = FluentPalette.TextPrimary
        };
        ToolTip.SetTip(openBtn, att.Uri);
        openBtn.Click += (_, _) =>
        {
            if (FileClickCommand?.CanExecute(att.Uri) == true)
            {
                FileClickCommand.Execute(att.Uri);
            }
        };

        var dlBtn = new Button
        {
            Content = "⬇",
            FontSize = 11.5,
            Padding = new Thickness(8, 5),
            CornerRadius = new CornerRadius(4),
            Background = FluentPalette.ControlFill,
            BorderBrush = FluentPalette.ControlStroke,
            BorderThickness = new Thickness(1),
            Foreground = FluentPalette.AccentText
        };
        ToolTip.SetTip(dlBtn, AppLocalization.T("Export / Save file", "导出 / 下载文件"));
        dlBtn.Click += (_, _) =>
        {
            if (DownloadCommand?.CanExecute(att.Uri) == true)
            {
                DownloadCommand.Execute(att.Uri);
            }
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 0, 8, 6),
            Children = { openBtn, dlBtn }
        };
    }
}
