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

    private readonly WrapPanel _root = new() { Orientation = Orientation.Horizontal };

    public AttachmentGalleryView()
    {
        Content = _root;
        ActualThemeVariantChanged += (_, _) => Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AttachmentsProperty || change.Property == MediaBytesLoaderProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _root.Children.Clear();
        var items = Attachments;
        if (items is null || items.Count == 0) return;

        foreach (var att in items)
        {
            if (att.Kind == AttachmentKind.Image)
            {
                _root.Children.Add(BuildImageTile(att));
            }
            else
            {
                _root.Children.Add(BuildFileTile(att));
            }
        }
    }

    private Control BuildImageTile(Attachment att)
    {
        var placeholder = new Border
        {
            MinWidth = 180,
            MinHeight = 110,
            MaxWidth = 360,
            MaxHeight = 260,
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Background = FluentPalette.CardSecondary,
            BorderBrush = FluentPalette.CardStroke,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 6),
            Child = new TextBlock
            {
                Text = AppLocalization.T($"Loading {att.Name}...", $"正在加载 {att.Name}..."),
                FontSize = 11.5,
                Foreground = FluentPalette.TextMuted,
                Margin = new Thickness(14, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

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
                            var img = new Image
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
                                CornerRadius = new CornerRadius(4),
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

                    placeholder.MinHeight = 48;
                    placeholder.Child = new TextBlock
                    {
                        Text = AppLocalization.T($"Image unavailable: {att.Name}", $"无法加载图片: {att.Name}"),
                        FontSize = 11.5,
                        Foreground = FluentPalette.TextMuted,
                        Margin = new Thickness(12, 8),
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
        var btn = new Button
        {
            Content = $"{icon} {att.Name}",
            FontSize = 12,
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 0, 8, 6),
            CornerRadius = new CornerRadius(4),
            Background = FluentPalette.ControlFill,
            BorderBrush = FluentPalette.ControlStroke,
            BorderThickness = new Thickness(1),
            Foreground = FluentPalette.TextPrimary
        };
        ToolTip.SetTip(btn, att.Uri);
        btn.Click += (_, _) =>
        {
            if (FileClickCommand?.CanExecute(att.Uri) == true)
            {
                FileClickCommand.Execute(att.Uri);
            }
        };
        return btn;
    }
}
