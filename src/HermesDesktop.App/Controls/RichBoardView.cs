using System.Text.Json;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Models;

namespace HermesDesktop.App.Controls;

/// <summary>
/// Renders rich interactive card boards (`cards` + `next` follow-up chips).
/// </summary>
public sealed class RichBoardView : UserControl
{
    public static readonly StyledProperty<ToolActivity?> ToolProperty =
        AvaloniaProperty.Register<RichBoardView, ToolActivity?>(nameof(Tool));

    public static readonly StyledProperty<ICommand?> FollowUpCommandProperty =
        AvaloniaProperty.Register<RichBoardView, ICommand?>(nameof(FollowUpCommand));

    public ToolActivity? Tool
    {
        get => GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    public ICommand? FollowUpCommand
    {
        get => GetValue(FollowUpCommandProperty);
        set => SetValue(FollowUpCommandProperty, value);
    }

    private readonly StackPanel _root = new() { Spacing = 8 };

    public RichBoardView()
    {
        Content = _root;
        ActualThemeVariantChanged += (_, _) => Render(Tool);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ToolProperty)
        {
            Render(change.GetNewValue<ToolActivity?>());
        }
    }

    private void Render(ToolActivity? tool)
    {
        _root.Children.Clear();
        if (tool is null) return;

        var boardObj = ParseBoardJson(tool);
        if (!boardObj.HasValue)
        {
            _root.Children.Add(new TextBlock
            {
                Text = tool.Status == ToolStatus.Running
                    ? AppLocalization.T("Preparing visual card...", "正在准备卡片...")
                    : AppLocalization.T("Card displayed", "已展示卡片"),
                FontSize = 12,
                Foreground = Brush.Parse("#99FFFFFF")
            });
            return;
        }

        var root = boardObj.Value;
        if (root.TryGetProperty("cards", out var cardsEl) && cardsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var card in cardsEl.EnumerateArray())
            {
                if (card.ValueKind == JsonValueKind.Object)
                {
                    _root.Children.Add(BuildCard(card));
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out _))
        {
            _root.Children.Add(BuildCard(root));
        }

        if (root.TryGetProperty("next", out var nextEl) && nextEl.ValueKind == JsonValueKind.Array)
        {
            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var n in nextEl.EnumerateArray())
            {
                var prompt = n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(prompt)) continue;

                var btn = new Button
                {
                    Content = prompt,
                    FontSize = 11.5,
                    Padding = new Thickness(10, 5),
                    Margin = new Thickness(0, 4, 6, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = FluentPalette.AccentSoft,
                    BorderBrush = FluentPalette.AccentStroke,
                    BorderThickness = new Thickness(1),
                    Foreground = FluentPalette.AccentText
                };
                btn.Click += (_, _) =>
                {
                    if (FollowUpCommand?.CanExecute(prompt) == true)
                    {
                        FollowUpCommand.Execute(prompt);
                    }
                };
                chips.Children.Add(btn);
            }
            if (chips.Children.Count > 0) _root.Children.Add(chips);
        }
    }

    private static JsonElement? ParseBoardJson(ToolActivity tool)
    {
        foreach (var raw in new[] { tool.Output, tool.Input })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("cards", out _) || doc.RootElement.TryGetProperty("type", out _))
                    {
                        return doc.RootElement.Clone();
                    }
                }
            }
            catch
            {
                // Try next source
            }
        }
        return null;
    }

    private Control BuildCard(JsonElement card)
    {
        var type = GetString(card, "type")?.ToLowerInvariant() ?? "card";
        var title = GetString(card, "title") ?? GetString(card, "heading");
        var subtitle = GetString(card, "subtitle") ?? GetString(card, "caption");

        var body = new StackPanel { Spacing = 8 };

        if (!string.IsNullOrWhiteSpace(title))
        {
            body.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = FluentPalette.TextPrimary,
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            body.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                Foreground = FluentPalette.TextSecondary,
                TextWrapping = TextWrapping.Wrap
            });
        }

        switch (type)
        {
            case "stats":
                BuildStatsContent(card, body);
                break;
            case "callout":
            case "status":
                BuildCalloutContent(card, body);
                break;
            case "table":
                BuildTableContent(card, body);
                break;
            case "list":
            case "checklist":
            case "steps":
            case "timeline":
                BuildListContent(card, body, type);
                break;
            case "progress":
                BuildProgressContent(card, body);
                break;
            case "composed":
                BuildComposedContent(card, body);
                break;
            default:
                BuildGenericFallback(card, body);
                break;
        }

        return new Border
        {
            Background = FluentPalette.CardFill,
            BorderBrush = FluentPalette.AccentStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 12),
            Child = body
        };
    }

    private static void BuildStatsContent(JsonElement card, StackPanel body)
    {
        if (!card.TryGetProperty("items", out var items) && !card.TryGetProperty("stats", out items)) return;
        if (items.ValueKind != JsonValueKind.Array) return;

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var label = GetString(item, "label") ?? GetString(item, "title") ?? "";
            var val = GetAnyString(item, "value") ?? "";
            var delta = GetString(item, "delta") ?? GetString(item, "note");
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = FluentPalette.TextMuted });
            stack.Children.Add(new TextBlock { Text = val, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = FluentPalette.AccentText });
            if (!string.IsNullOrWhiteSpace(delta))
            {
                stack.Children.Add(new TextBlock { Text = delta, FontSize = 11, Foreground = FluentPalette.MathText });
            }

            wrap.Children.Add(new Border
            {
                Background = FluentPalette.CardSecondary,
                BorderBrush = FluentPalette.CardStroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8),
                Margin = new Thickness(0, 0, 8, 8),
                MinWidth = 110,
                Child = stack
            });
        }
        body.Children.Add(wrap);
    }

    private static void BuildCalloutContent(JsonElement card, StackPanel body)
    {
        var text = GetString(card, "body") ?? GetString(card, "message") ?? GetString(card, "text") ?? GetString(card, "summary");
        if (!string.IsNullOrWhiteSpace(text))
        {
            body.Children.Add(new SelectableTextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 19,
                Foreground = FluentPalette.TextPrimary
            });
        }
    }

    private static void BuildTableContent(JsonElement card, StackPanel body)
    {
        var cols = new List<string>();
        if (card.TryGetProperty("columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in colsEl.EnumerateArray())
            {
                cols.Add(c.ValueKind == JsonValueKind.Object
                    ? (GetString(c, "label") ?? GetString(c, "title") ?? GetString(c, "key") ?? "")
                    : (c.ToString()));
            }
        }

        if (!card.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array) return;
        var grid = new Grid();
        var colCount = cols.Count;
        if (colCount == 0 && rowsEl.GetArrayLength() > 0 && rowsEl[0].ValueKind == JsonValueKind.Array)
            colCount = rowsEl[0].GetArrayLength();
        if (colCount == 0) return;

        for (var c = 0; c < colCount; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        if (cols.Count > 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var _ in rowsEl.EnumerateArray()) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var rowOffset = 0;
        if (cols.Count > 0)
        {
            for (var c = 0; c < colCount; c++)
            {
                var cell = new Border
                {
                    Background = FluentPalette.TableHeaderBack,
                    BorderBrush = FluentPalette.TableBorder,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(10, 6),
                    Child = new TextBlock { Text = cols[c], FontWeight = FontWeight.SemiBold, FontSize = 12, Foreground = FluentPalette.TextPrimary }
                };
                cell[Grid.RowProperty] = 0;
                cell[Grid.ColumnProperty] = c;
                grid.Children.Add(cell);
            }
            rowOffset = 1;
        }

        var r = 0;
        foreach (var row in rowsEl.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array) { r++; continue; }
            for (var c = 0; c < Math.Min(colCount, row.GetArrayLength()); c++)
            {
                var cell = new Border
                {
                    Background = r % 2 == 0 ? FluentPalette.TableRowEven : FluentPalette.TableRowOdd,
                    BorderBrush = FluentPalette.TableBorder,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(10, 5),
                    Child = new SelectableTextBlock { Text = row[c].ToString(), FontSize = 12, Foreground = FluentPalette.TextSecondary, TextWrapping = TextWrapping.Wrap }
                };
                cell[Grid.RowProperty] = r + rowOffset;
                cell[Grid.ColumnProperty] = c;
                grid.Children.Add(cell);
            }
            r++;
        }

        body.Children.Add(new Border
        {
            BorderBrush = FluentPalette.TableBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = grid
            }
        });
    }

    private static void BuildListContent(JsonElement card, StackPanel body, string type)
    {
        if (!card.TryGetProperty("items", out var itemsEl) && !card.TryGetProperty("steps", out itemsEl)) return;
        if (itemsEl.ValueKind != JsonValueKind.Array) return;

        var idx = 1;
        foreach (var item in itemsEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                body.Children.Add(new TextBlock { Text = $"• {item.GetString()}", FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var label = GetString(item, "title") ?? GetString(item, "label") ?? GetString(item, "text") ?? "";
                var detail = GetString(item, "detail") ?? GetString(item, "subtitle") ?? GetString(item, "description");
                var done = item.TryGetProperty("done", out var dEl) && dEl.ValueKind == JsonValueKind.True;
                var prefix = type == "steps" ? $"{idx++}." : done ? "☑" : "•";

                var row = new StackPanel { Spacing = 2 };
                row.Children.Add(new TextBlock
                {
                    Text = $"{prefix} {label}",
                    FontSize = 12.5,
                    FontWeight = FontWeight.Medium,
                    Foreground = done ? FluentPalette.TextMuted : FluentPalette.TextPrimary,
                    TextWrapping = TextWrapping.Wrap
                });
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = detail,
                        FontSize = 11.5,
                        Foreground = FluentPalette.TextMuted,
                        Margin = new Thickness(14, 0, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                body.Children.Add(row);
            }
        }
    }

    private static void BuildProgressContent(JsonElement card, StackPanel body)
    {
        var value = card.TryGetProperty("value", out var vEl) && vEl.ValueKind == JsonValueKind.Number ? vEl.GetDouble() : 0;
        var max = card.TryGetProperty("max", out var mEl) && mEl.ValueKind == JsonValueKind.Number ? mEl.GetDouble() : 100;
        body.Children.Add(new ProgressBar
        {
            Value = value,
            Maximum = max > 0 ? max : 100,
            Height = 6,
            Foreground = Brush.Parse("#E5A93B")
        });
    }

    private static void BuildComposedContent(JsonElement card, StackPanel body)
    {
        if (!card.TryGetProperty("sections", out var secs) || secs.ValueKind != JsonValueKind.Array)
        {
            BuildGenericFallback(card, body);
            return;
        }
        foreach (var sec in secs.EnumerateArray())
        {
            if (sec.ValueKind != JsonValueKind.Object) continue;
            var text = GetString(sec, "text") ?? GetString(sec, "body") ?? GetString(sec, "title");
            if (!string.IsNullOrWhiteSpace(text))
            {
                body.Children.Add(new SelectableTextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            }
        }
    }

    private static void BuildGenericFallback(JsonElement card, StackPanel body)
    {
        var summary = GetString(card, "body") ?? GetString(card, "text") ?? GetString(card, "summary") ?? GetString(card, "description");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            body.Children.Add(new SelectableTextBlock { Text = summary, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        }
    }

    private static string? GetString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static string? GetAnyString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? el.ToString()
            : null;
}
