using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HermesDesktop.Core.Diagnostics;
using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Markdown;

namespace HermesDesktop.App.Controls;

public sealed class MarkdownView : UserControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public static readonly StyledProperty<ICommand?> PreviewCommandProperty =
        AvaloniaProperty.Register<MarkdownView, ICommand?>(nameof(PreviewCommand));

    public static readonly StyledProperty<ICommand?> BrowserCommandProperty =
        AvaloniaProperty.Register<MarkdownView, ICommand?>(nameof(BrowserCommand));

    public static readonly StyledProperty<Func<string, Task<byte[]?>>?> MediaBytesLoaderProperty =
        AvaloniaProperty.Register<MarkdownView, Func<string, Task<byte[]?>>?>(nameof(MediaBytesLoader));

    public static readonly StyledProperty<ICommand?> ImageViewerCommandProperty =
        AvaloniaProperty.Register<MarkdownView, ICommand?>(nameof(ImageViewerCommand));

    public static readonly StyledProperty<ICommand?> DownloadCommandProperty =
        AvaloniaProperty.Register<MarkdownView, ICommand?>(nameof(DownloadCommand));

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public ICommand? PreviewCommand
    {
        get => GetValue(PreviewCommandProperty);
        set => SetValue(PreviewCommandProperty, value);
    }

    public ICommand? BrowserCommand
    {
        get => GetValue(BrowserCommandProperty);
        set => SetValue(BrowserCommandProperty, value);
    }

    public Func<string, Task<byte[]?>>? MediaBytesLoader
    {
        get => GetValue(MediaBytesLoaderProperty);
        set => SetValue(MediaBytesLoaderProperty, value);
    }

    public ICommand? ImageViewerCommand
    {
        get => GetValue(ImageViewerCommandProperty);
        set => SetValue(ImageViewerCommandProperty, value);
    }

    public ICommand? DownloadCommand
    {
        get => GetValue(DownloadCommandProperty);
        set => SetValue(DownloadCommandProperty, value);
    }

    private readonly StackPanel _root = new() { Spacing = 8 };
    private readonly List<string> _renderedBlockKeys = [];
    private string? _lastMarkdown;

    private const int InitialTableRowLimit = 40;

    private static readonly Regex CodeTokenRegex = new(
        @"(//[^\n]*|#[^\n]*|""(?:\\.|[^""\n])*""|'(?:\\.|[^'\n])*'|\b(?:async|await|break|case|catch|class|const|continue|def|default|elif|else|enum|export|false|finally|for|from|func|function|if|import|in|interface|let|namespace|new|None|null|override|private|public|raise|record|return|sealed|static|struct|switch|throw|true|True|False|try|using|var|void|while|yield|SELECT|FROM|WHERE|INSERT|UPDATE|DELETE|JOIN|GROUP BY|ORDER BY)\b|\b\d+(?:\.\d+)?\b)",
        RegexOptions.Compiled);

    public MarkdownView()
    {
        Content = _root;
        ActualThemeVariantChanged += (_, _) => ForceRerender();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
        {
            var next = change.GetNewValue<string?>() ?? string.Empty;
            if (!string.Equals(_lastMarkdown, next, StringComparison.Ordinal))
            {
                _lastMarkdown = next;
                RenderIncremental(next);
            }
        }
        else if (change.Property == MediaBytesLoaderProperty
            && _lastMarkdown is not null
            && (_lastMarkdown.Contains("MEDIA:", StringComparison.Ordinal) || _lastMarkdown.Contains("![", StringComparison.Ordinal)))
        {
            ForceRerender();
        }
    }

    private void ForceRerender()
    {
        if (string.IsNullOrWhiteSpace(_lastMarkdown)) return;
        _renderedBlockKeys.Clear();
        _root.Children.Clear();
        RenderIncremental(_lastMarkdown);
    }

    private void RenderIncremental(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _renderedBlockKeys.Clear();
            _root.Children.Clear();
            return;
        }

        AppDiagnostics.SetPhase("MarkdownView.Parse", $"len={text.Length}");
        var blocks = MarkdownBlockParser.Parse(text);

        var common = 0;
        var maxCommon = Math.Min(_renderedBlockKeys.Count, blocks.Count);
        while (common < maxCommon && string.Equals(_renderedBlockKeys[common], blocks[common].Key, StringComparison.Ordinal))
        {
            common++;
        }

        while (_root.Children.Count > common)
        {
            _root.Children.RemoveAt(_root.Children.Count - 1);
            _renderedBlockKeys.RemoveAt(_renderedBlockKeys.Count - 1);
        }

        for (var i = common; i < blocks.Count; i++)
        {
            var block = blocks[i];
            AppDiagnostics.SetPhase($"MarkdownView.Build:{block.Kind}", block.Key);
            _root.Children.Add(BuildBlockControl(block));
            _renderedBlockKeys.Add(block.Key);
        }
        AppDiagnostics.SetPhase("idle");
    }

    private Control BuildBlockControl(MarkdownBlock block)
    {
        return block.Kind switch
        {
            MarkdownBlockKind.PreviewFile => BuildPreviewCard(block.Text),
            MarkdownBlockKind.Image => BuildInlineImageCard(block.Text, block.CodeLanguage),
            MarkdownBlockKind.CodeBlock => BuildCodeBlock(block.CodeLanguage, block.Text),
            MarkdownBlockKind.MathBlock => BuildMathBlock(block.Text),
            MarkdownBlockKind.Table => BuildTable(block.TableHeaders ?? [], block.TableRows ?? []),
            MarkdownBlockKind.HorizontalRule => new Border
            {
                Height = 1,
                Background = FluentPalette.TableBorder,
                Margin = new Thickness(0, 4)
            },
            MarkdownBlockKind.Heading => BuildHeading(block.HeadingLevel, block.Text),
            MarkdownBlockKind.Quote => BuildQuote(block.Text),
            MarkdownBlockKind.List => BuildList(block.ListItems ?? []),
            _ => CreateRichTextBlock(block.Text)
        };
    }

    private static Control BuildHeading(int level, string text)
    {
        var tb = CreateRichTextBlock(text);
        tb.FontSize = level switch { 1 => 18, 2 => 16, 3 => 14.5, _ => 13.5 };
        tb.FontWeight = FontWeight.SemiBold;
        tb.Foreground = FluentPalette.TextPrimary;
        tb.Margin = new Thickness(0, 4, 0, 2);
        return tb;
    }

    private static Control BuildQuote(string text)
    {
        var quoteTb = CreateRichTextBlock(text);
        quoteTb.Foreground = FluentPalette.TextSecondary;
        return new Border
        {
            BorderBrush = FluentPalette.AccentStroke,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = FluentPalette.AccentSoft,
            CornerRadius = new CornerRadius(0, 4, 4, 0),
            Padding = new Thickness(12, 8),
            Child = quoteTb
        };
    }

    private static Control BuildList(IReadOnlyList<MarkdownListItem> items)
    {
        var listPanel = new StackPanel { Spacing = 5 };
        foreach (var item in items)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            if (item.IsTask)
            {
                var box = new Border
                {
                    Width = 15,
                    Height = 15,
                    CornerRadius = new CornerRadius(3),
                    Background = item.IsChecked ? FluentPalette.AccentFill : FluentPalette.ControlFill,
                    BorderBrush = item.IsChecked ? FluentPalette.AccentFill : FluentPalette.ControlStroke,
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2, 3, 8, 0),
                    [Grid.ColumnProperty] = 0
                };
                if (item.IsChecked)
                {
                    box.Child = new TextBlock
                    {
                        Text = "✓",
                        FontSize = 10.5,
                        FontWeight = FontWeight.Bold,
                        Foreground = FluentPalette.AccentForeground,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                row.Children.Add(box);
            }
            else
            {
                row.Children.Add(new TextBlock
                {
                    Text = item.Marker,
                    Foreground = FluentPalette.AccentText,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 13.5,
                    LineHeight = 21,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(4, 0, 8, 0),
                    [Grid.ColumnProperty] = 0
                });
            }
            var itemTb = CreateRichTextBlock(item.Body);
            itemTb[Grid.ColumnProperty] = 1;
            row.Children.Add(itemTb);
            listPanel.Children.Add(row);
        }
        return listPanel;
    }

    private static SelectableTextBlock CreateLightweightCellText(string raw, bool isHeader)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 18,
            FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = isHeader ? FluentPalette.TextPrimary : FluentPalette.TextSecondary
        };
        if (!HasInlineSyntax(raw))
        {
            tb.Text = raw;
            return tb;
        }
        PopulateInlines(tb.Inlines!, raw, allowUiContainers: false);
        return tb;
    }

    private static SelectableTextBlock CreateRichTextBlock(string raw)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            LineHeight = 21,
            Foreground = FluentPalette.TextPrimary
        };

        if (!HasInlineSyntax(raw))
        {
            tb.Text = raw;
            return tb;
        }

        PopulateInlines(tb.Inlines!, raw, allowUiContainers: true);
        if (tb.Inlines!.OfType<InlineUIContainer>().Any())
        {
            // Let Avalonia TextLayout expand line height automatically for inline fractions, roots, and vectors.
            tb.LineHeight = double.NaN;
            tb.Margin = new Thickness(0, 2);
        }
        return tb;
    }

    private static bool HasInlineSyntax(string raw) =>
        raw.IndexOfAny(['*', '`', '[', '\\', '$', '~']) >= 0 || raw.Contains("http", StringComparison.OrdinalIgnoreCase);

    private static void PopulateInlines(InlineCollection inlines, string raw, bool allowUiContainers)
    {
        var pos = 0;
        var textBuf = new System.Text.StringBuilder();

        void FlushPlain()
        {
            if (textBuf.Length > 0)
            {
                inlines.Add(new Run(textBuf.ToString()));
                textBuf.Clear();
            }
        }

        while (pos < raw.Length)
        {
            var ch = raw[pos];

            // 1. Inline code `...`
            if (ch == '`')
            {
                var close = raw.IndexOf('`', pos + 1);
                if (close > pos + 1 && raw.AsSpan(pos + 1, close - pos - 1).IndexOf('\n') < 0)
                {
                    FlushPlain();
                    inlines.Add(new Run($" {raw[(pos + 1)..close]} ")
                    {
                        FontFamily = FluentPalette.MonoFont,
                        FontSize = 12.5,
                        Background = FluentPalette.InlineCodeBack,
                        Foreground = FluentPalette.InlineCodeText
                    });
                    pos = close + 1;
                    continue;
                }
            }

            // 2. Inline LaTeX math $...$
            if (ch == '$' && LatexMathEngine.IsInlineDollarOpeningAllowed(raw, pos))
            {
                var close = LatexMathEngine.FindClosingInlineDollar(raw, pos);
                if (close > pos + 1)
                {
                    FlushPlain();
                    var tex = raw[(pos + 1)..close];
                    AppendInlineMath(inlines, tex, allowUiContainers);
                    pos = close + 1;
                    continue;
                }
            }

            // 3. Inline LaTeX math \(...\)
            if (ch == '\\' && pos + 1 < raw.Length && raw[pos + 1] == '(')
            {
                var close = raw.IndexOf(@"\)", pos + 2, StringComparison.Ordinal);
                if (close > pos + 2 && close - pos <= 400)
                {
                    FlushPlain();
                    var tex = raw[(pos + 2)..close];
                    AppendInlineMath(inlines, tex, allowUiContainers);
                    pos = close + 2;
                    continue;
                }
            }

            // 4. Bold + Italic ***...***
            if (raw.AsSpan(pos).StartsWith("***", StringComparison.Ordinal))
            {
                var close = raw.IndexOf("***", pos + 3, StringComparison.Ordinal);
                if (close > pos + 3 && raw.AsSpan(pos + 3, close - pos - 3).IndexOf('\n') < 0)
                {
                    FlushPlain();
                    inlines.Add(new Run(raw[(pos + 3)..close])
                    {
                        FontWeight = FontWeight.SemiBold,
                        FontStyle = FontStyle.Italic,
                        Foreground = FluentPalette.TextPrimary
                    });
                    pos = close + 3;
                    continue;
                }
            }

            // 5. Bold **...**
            if (raw.AsSpan(pos).StartsWith("**", StringComparison.Ordinal))
            {
                var close = raw.IndexOf("**", pos + 2, StringComparison.Ordinal);
                if (close > pos + 2 && raw.AsSpan(pos + 2, close - pos - 2).IndexOf('\n') < 0)
                {
                    FlushPlain();
                    inlines.Add(new Run(raw[(pos + 2)..close])
                    {
                        FontWeight = FontWeight.SemiBold,
                        Foreground = FluentPalette.TextPrimary
                    });
                    pos = close + 2;
                    continue;
                }
            }

            // 6. Strikethrough ~~...~~
            if (raw.AsSpan(pos).StartsWith("~~", StringComparison.Ordinal))
            {
                var close = raw.IndexOf("~~", pos + 2, StringComparison.Ordinal);
                if (close > pos + 2 && raw.AsSpan(pos + 2, close - pos - 2).IndexOf('\n') < 0)
                {
                    FlushPlain();
                    inlines.Add(new Run(raw[(pos + 2)..close])
                    {
                        Foreground = FluentPalette.TextMuted,
                        TextDecorations = TextDecorations.Strikethrough
                    });
                    pos = close + 2;
                    continue;
                }
            }

            // 7. Italic *...*
            if (ch == '*' && pos + 1 < raw.Length && !char.IsWhiteSpace(raw[pos + 1]))
            {
                var close = raw.IndexOf('*', pos + 1);
                if (close > pos + 1 && !char.IsWhiteSpace(raw[close - 1]) && raw.AsSpan(pos + 1, close - pos - 1).IndexOf('\n') < 0)
                {
                    FlushPlain();
                    inlines.Add(new Run(raw[(pos + 1)..close]) { FontStyle = FontStyle.Italic });
                    pos = close + 1;
                    continue;
                }
            }

            // 8. Markdown link [label](url)
            if (ch == '[')
            {
                var closeBracket = raw.IndexOf(']', pos + 1);
                if (closeBracket > pos + 1 && closeBracket + 1 < raw.Length && raw[closeBracket + 1] == '(')
                {
                    var closeParen = raw.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 2
                        && raw.AsSpan(pos + 1, closeBracket - pos - 1).IndexOf('\n') < 0
                        && raw.AsSpan(closeBracket + 2, closeParen - closeBracket - 2).IndexOf('\n') < 0)
                    {
                        var label = raw[(pos + 1)..closeBracket];
                        var url = raw[(closeBracket + 2)..closeParen].Trim();
                        FlushPlain();
                        AppendLinkInline(inlines, label, url, allowUiContainers);
                        pos = closeParen + 1;
                        continue;
                    }
                }
            }

            // 9. Bare https:// / http:// link
            if ((ch == 'h' || ch == 'H') &&
                (raw.AsSpan(pos).StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                 || raw.AsSpan(pos).StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            {
                var end = pos;
                while (end < raw.Length && !char.IsWhiteSpace(raw[end]) && raw[end] is not (')' or ']' or '>' or '"' or '\'' or ','))
                {
                    end++;
                }
                if (end > pos + 8)
                {
                    var url = raw[pos..end];
                    FlushPlain();
                    AppendLinkInline(inlines, url, url, allowUiContainers);
                    pos = end;
                    continue;
                }
            }

            textBuf.Append(ch);
            pos++;
        }

        FlushPlain();
    }

    private static void AppendInlineMath(InlineCollection inlines, string tex, bool allowUiContainers)
    {
        try
        {
            var ast = LatexMathEngine.Parse(tex);
            if (allowUiContainers && LatexMathEngine.RequiresTwoDimensionalLayout(ast))
            {
                var ctrl = MathFormulaView.CreateInline(ast, baseFontSize: 13.5);
                inlines.Add(new InlineUIContainer(ctrl)
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
                return;
            }

            if (LatexMathEngine.TryFormatSimpleInline(ast, out var formatted))
            {
                inlines.Add(new Run(formatted)
                {
                    FontFamily = FluentPalette.MathSerifFont,
                    FontStyle = FontStyle.Italic,
                    Foreground = FluentPalette.MathText
                });
                return;
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("MATH_INLINE_ERR", ex.Message);
        }

        inlines.Add(new Run(tex)
        {
            FontFamily = FluentPalette.MathSerifFont,
            FontStyle = FontStyle.Italic,
            Foreground = FluentPalette.MathText
        });
    }

    private static void AppendLinkInline(InlineCollection inlines, string label, string url, bool allowUiContainers)
    {
        var isSafeHttp = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        if (allowUiContainers && isSafeHttp && uri is not null)
        {
            var linkTb = new TextBlock
            {
                Text = label,
                Foreground = FluentPalette.LinkText,
                TextDecorations = TextDecorations.Underline,
                FontSize = 13.5,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            linkTb.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                try
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch { /* Ignore launch failures */ }
            };
            inlines.Add(new InlineUIContainer(linkTb) { BaselineAlignment = BaselineAlignment.Baseline });
            return;
        }

        inlines.Add(new Run(label)
        {
            Foreground = FluentPalette.LinkText,
            TextDecorations = TextDecorations.Underline
        });
    }

    private Control BuildMathBlock(string tex)
    {
        var copyBtn = new Button
        {
            Content = AppLocalization.T("Copy", "复制"),
            FontSize = 11,
            Padding = new Thickness(8, 2),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            BorderBrush = FluentPalette.CardStroke,
            BorderThickness = new Thickness(1),
            Foreground = FluentPalette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        ToolTip.SetTip(copyBtn, AppLocalization.T("Copy LaTeX formula", "复制 LaTeX 公式"));
        copyBtn.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is not null)
            {
                await top.Clipboard.SetTextAsync(tex);
                copyBtn.Content = AppLocalization.T("Copied", "已复制");
            }
        };

        Control formulaCtrl;
        try
        {
            formulaCtrl = MathFormulaView.CreateDisplay(tex, baseFontSize: 16);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("MATH_BLOCK_ERR", ex.Message);
            formulaCtrl = new SelectableTextBlock
            {
                Text = tex,
                FontFamily = FluentPalette.MonoFont,
                FontSize = 13,
                Foreground = FluentPalette.TextPrimary,
                TextWrapping = TextWrapping.Wrap
            };
        }
        formulaCtrl.HorizontalAlignment = HorizontalAlignment.Center;

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Border
            {
                Padding = new Thickness(4, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = formulaCtrl
            },
            [Grid.ColumnProperty] = 0
        };
        copyBtn[Grid.ColumnProperty] = 1;
        layout.Children.Add(scroller);
        layout.Children.Add(copyBtn);

        return new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 6),
            Margin = new Thickness(0, 2),
            Child = layout
        };
    }

    private Control BuildCodeBlock(string lang, string code)
    {
        var copyBtn = new Button
        {
            Content = AppLocalization.T("Copy", "复制"),
            FontSize = 11,
            Padding = new Thickness(8, 3),
            CornerRadius = new CornerRadius(4),
            Background = FluentPalette.ControlFill,
            BorderBrush = FluentPalette.ControlStroke,
            BorderThickness = new Thickness(1),
            Foreground = FluentPalette.TextPrimary
        };
        copyBtn.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is not null)
            {
                await top.Clipboard.SetTextAsync(code);
                copyBtn.Content = AppLocalization.T("Copied", "已复制");
            }
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(lang) ? "code" : lang,
            FontSize = 11,
            Foreground = FluentPalette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            [Grid.ColumnProperty] = 0
        });
        copyBtn[Grid.ColumnProperty] = 1;
        header.Children.Add(copyBtn);

        var codeText = new SelectableTextBlock
        {
            FontFamily = FluentPalette.MonoFont,
            FontSize = 12.5,
            LineHeight = 18,
            Foreground = FluentPalette.TextPrimary,
            TextWrapping = TextWrapping.NoWrap
        };
        PopulateCodeHighlights(codeText, code);

        return new Border
        {
            Background = FluentPalette.CodeBack,
            BorderBrush = FluentPalette.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    header,
                    new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        AllowAutoHide = false,
                        Padding = new Thickness(0, 0, 0, 14),
                        Content = codeText
                    }
                }
            }
        };
    }

    private static void PopulateCodeHighlights(SelectableTextBlock tb, string code)
    {
        if (code.Length > 20_000)
        {
            tb.Text = code;
            return;
        }

        var inlines = tb.Inlines!;
        var last = 0;
        foreach (Match m in CodeTokenRegex.Matches(code))
        {
            if (m.Index > last)
                inlines.Add(new Run(code[last..m.Index]));

            var tok = m.Value;
            IBrush brush = tok[0] switch
            {
                '/' or '#' => FluentPalette.SyntaxComment,
                '"' or '\'' => FluentPalette.SyntaxString,
                >= '0' and <= '9' => FluentPalette.SyntaxNumber,
                _ => FluentPalette.SyntaxKeyword
            };
            inlines.Add(new Run(tok) { Foreground = brush });
            last = m.Index + m.Length;
        }
        if (last < code.Length)
            inlines.Add(new Run(code[last..]));
    }

    private static Control BuildTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var colCount = Math.Max(headers.Count, rows.Count > 0 ? rows.Max(r => r.Count) : 0);
        if (colCount == 0) return new Border();

        var container = new StackPanel { Spacing = 6 };
        var shownLimit = rows.Count > InitialTableRowLimit ? InitialTableRowLimit : rows.Count;

        Grid CreateTableGrid(int rowCountToRender)
        {
            var grid = new Grid();
            for (var c = 0; c < colCount; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            }
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var r = 0; r < rowCountToRender; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            grid.Children.Add(new Border
            {
                Background = FluentPalette.TableHeaderBack,
                BorderBrush = FluentPalette.TableBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                [Grid.RowProperty] = 0,
                [Grid.ColumnProperty] = 0,
                [Grid.ColumnSpanProperty] = colCount
            });

            var maxCellWidth = colCount <= 3 ? 340d : 240d;
            for (var c = 0; c < colCount; c++)
            {
                var cellText = CreateLightweightCellText(c < headers.Count ? headers[c] : "", isHeader: true);
                cellText.MaxWidth = maxCellWidth;
                cellText.Margin = new Thickness(10, 7);
                cellText[Grid.RowProperty] = 0;
                cellText[Grid.ColumnProperty] = c;
                grid.Children.Add(cellText);
            }

            for (var r = 0; r < rowCountToRender; r++)
            {
                grid.Children.Add(new Border
                {
                    Background = r % 2 == 0 ? FluentPalette.TableRowEven : FluentPalette.TableRowOdd,
                    BorderBrush = FluentPalette.TableBorder,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    [Grid.RowProperty] = r + 1,
                    [Grid.ColumnProperty] = 0,
                    [Grid.ColumnSpanProperty] = colCount
                });

                for (var c = 0; c < colCount; c++)
                {
                    var text = c < rows[r].Count ? rows[r][c] : "";
                    var cellText = CreateLightweightCellText(text, isHeader: false);
                    cellText.MaxWidth = maxCellWidth;
                    cellText.Margin = new Thickness(10, 6);
                    cellText[Grid.RowProperty] = r + 1;
                    cellText[Grid.ColumnProperty] = c;
                    grid.Children.Add(cellText);
                }
            }
            return grid;
        }

        var tableBorder = new Border
        {
            BorderBrush = FluentPalette.TableBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                AllowAutoHide = false,
                Padding = new Thickness(0, 0, 0, 14),
                Content = CreateTableGrid(shownLimit)
            }
        };
        container.Children.Add(tableBorder);

        if (rows.Count > InitialTableRowLimit)
        {
            var expandBtn = new Button
            {
                Content = AppLocalization.T(
                    $"Show all {rows.Count} rows ({rows.Count - InitialTableRowLimit} more)",
                    $"展开全部 {rows.Count} 行（其余 {rows.Count - InitialTableRowLimit} 行）"),
                FontSize = 11.5,
                Padding = new Thickness(10, 5),
                CornerRadius = new CornerRadius(4),
                Background = FluentPalette.ControlFill,
                BorderBrush = FluentPalette.ControlStroke,
                BorderThickness = new Thickness(1),
                Foreground = FluentPalette.AccentText,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            expandBtn.Click += (_, _) =>
            {
                tableBorder.Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    AllowAutoHide = false,
                    Padding = new Thickness(0, 0, 0, 14),
                    Content = CreateTableGrid(rows.Count)
                };
                expandBtn.IsVisible = false;
            };
            container.Children.Add(expandBtn);
        }

        return container;
    }

    private Control BuildPreviewCard(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName)) fileName = filePath;

        var isHtml = filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

        var previewBtn = new Button
        {
            Content = isHtml ? AppLocalization.T("Preview Here", "应用内预览") : AppLocalization.T("Open File", "打开文件"),
            FontSize = 11.5,
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(4),
            Background = FluentPalette.AccentFill,
            Foreground = FluentPalette.AccentForeground,
            FontWeight = FontWeight.SemiBold
        };
        previewBtn.Click += (_, _) =>
        {
            if (PreviewCommand?.CanExecute(filePath) == true)
            {
                PreviewCommand.Execute(filePath);
            }
        };

        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto") };
        headerRow.Children.Add(new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(4),
            Background = FluentPalette.AccentSoft,
            Margin = new Thickness(0, 0, 10, 0),
            [Grid.ColumnProperty] = 0,
            Child = new TextBlock
            {
                Text = isHtml ? "</>" : "📄",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = FluentPalette.AccentText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        headerRow.Children.Add(new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            [Grid.ColumnProperty] = 1,
            Children =
            {
                new TextBlock { Text = fileName, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = FluentPalette.TextPrimary },
                new TextBlock { Text = filePath, FontSize = 11, Foreground = FluentPalette.TextMuted, TextTrimming = TextTrimming.CharacterEllipsis }
            }
        });

        if (isHtml)
        {
            var browserBtn = new Button
            {
                Content = AppLocalization.T("Browser", "浏览器打开"),
                FontSize = 11.5,
                Padding = new Thickness(10, 6),
                CornerRadius = new CornerRadius(4),
                Background = FluentPalette.ControlFill,
                BorderBrush = FluentPalette.ControlStroke,
                BorderThickness = new Thickness(1),
                Foreground = FluentPalette.TextPrimary,
                Margin = new Thickness(8, 0, 6, 0)
            };
            browserBtn.Click += (_, _) =>
            {
                if (BrowserCommand?.CanExecute(filePath) == true)
                {
                    BrowserCommand.Execute(filePath);
                }
            };
            browserBtn[Grid.ColumnProperty] = 2;
            headerRow.Children.Add(browserBtn);
        }

        var downloadBtn = new Button
        {
            Content = AppLocalization.T("Export", "导出"),
            FontSize = 11.5,
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(4),
            Background = FluentPalette.ControlFill,
            BorderBrush = FluentPalette.ControlStroke,
            BorderThickness = new Thickness(1),
            Foreground = FluentPalette.TextPrimary,
            Margin = new Thickness(8, 0, 6, 0)
        };
        ToolTip.SetTip(downloadBtn, AppLocalization.T("Export / Save file to disk", "导出或下载此文件到本地"));
        downloadBtn.Click += (_, _) =>
        {
            if (DownloadCommand?.CanExecute(filePath) == true)
            {
                DownloadCommand.Execute(filePath);
            }
        };
        downloadBtn[Grid.ColumnProperty] = 3;
        headerRow.Children.Add(downloadBtn);

        previewBtn[Grid.ColumnProperty] = 4;
        headerRow.Children.Add(previewBtn);

        return new Border
        {
            Background = FluentPalette.CardSecondary,
            BorderBrush = FluentPalette.AccentStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10),
            Child = headerRow
        };
    }

    private Control BuildInlineImageCard(string pathOrUrl, string alt)
    {
        var name = Path.GetFileName(pathOrUrl.Split('?')[0]);
        if (string.IsNullOrWhiteSpace(name)) name = string.IsNullOrWhiteSpace(alt) ? "Image" : alt;

        var frame = new Border
        {
            MinWidth = 200,
            MinHeight = 110,
            MaxWidth = 460,
            MaxHeight = 320,
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Background = FluentPalette.CardSecondary,
            BorderBrush = FluentPalette.CardStroke,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = AppLocalization.T($"Loading {name}...", $"正在加载 {name}..."),
                FontSize = 12,
                Foreground = FluentPalette.TextMuted,
                Margin = new Thickness(14, 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var loader = MediaBytesLoader;
        if (loader is not null)
        {
            _ = Task.Run(async () =>
            {
                var bytes = await loader(pathOrUrl);
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
                                MaxWidth = 460,
                                MaxHeight = 320,
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
                            ToolTip.SetTip(btn, AppLocalization.T($"{name} — Click to enlarge", $"{name} — 点击查看大图"));
                            btn.Click += (_, _) =>
                            {
                                if (ImageViewerCommand?.CanExecute(pathOrUrl) == true)
                                {
                                    ImageViewerCommand.Execute(pathOrUrl);
                                }
                            };
                            frame.MinWidth = 0;
                            frame.MinHeight = 0;
                            frame.Child = btn;
                            return;
                        }
                        catch
                        {
                            // Fallback below
                        }
                    }

                    frame.MinHeight = 48;
                    frame.Child = new TextBlock
                    {
                        Text = AppLocalization.T($"Image unavailable: {name}", $"无法加载图片: {name}"),
                        FontSize = 12,
                        Foreground = FluentPalette.TextMuted,
                        Margin = new Thickness(12, 8),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                });
            });
        }

        return frame;
    }
}
