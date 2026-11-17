using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HermesDesktop.Core.Markdown;
using PathShape = Avalonia.Controls.Shapes.Path;

namespace HermesDesktop.App.Controls;

public static class MathFormulaView
{
    public static Control CreateDisplay(string latex, double baseFontSize = 15.5)
    {
        var ast = LatexMathEngine.Parse(latex);
        return RenderNode(ast, baseFontSize, isDisplay: true);
    }

    public static Control CreateInline(MathNode ast, double baseFontSize = 13.5)
    {
        return RenderNode(ast, baseFontSize, isDisplay: false);
    }

    private static Control RenderNode(MathNode node, double fontSize, bool isDisplay)
    {
        return node switch
        {
            MathSequenceNode seq => RenderSequence(seq, fontSize, isDisplay),
            MathTextNode txt => RenderText(txt, fontSize),
            MathFractionNode frac => RenderFraction(frac, fontSize, isDisplay),
            MathRootNode root => RenderRoot(root, fontSize, isDisplay),
            MathScriptNode script => RenderScript(script, fontSize, isDisplay),
            MathAccentNode accent => RenderAccent(accent, fontSize, isDisplay),
            MathDelimitedNode delim => RenderDelimited(delim, fontSize, isDisplay),
            MathMatrixNode matrix => RenderMatrix(matrix, fontSize, isDisplay),
            MathStackNode stack => RenderStack(stack, fontSize, isDisplay),
            _ => new TextBlock { Text = "" }
        };
    }

    private static Control RenderSequence(MathSequenceNode seq, double fontSize, bool isDisplay)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 0.5
        };
        foreach (var item in seq.Items)
        {
            panel.Children.Add(RenderNode(item, fontSize, isDisplay));
        }
        return panel;
    }

    private static Control RenderText(MathTextNode txt, double fontSize)
    {
        var raw = txt.Text;
        var trimmed = raw.Trim();
        var isSpacedOperator = raw.Length > trimmed.Length && trimmed.Length > 0;

        var tb = new TextBlock
        {
            Text = isSpacedOperator ? trimmed : raw,
            FontSize = fontSize,
            Foreground = FluentPalette.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isSpacedOperator)
        {
            var pad = trimmed is "," ? 0 : Math.Max(3, fontSize * 0.24);
            tb.Margin = new Thickness(trimmed is "," ? 0 : pad, 0, pad, 0);
        }

        switch (txt.Style)
        {
            case MathTextStyle.Italic:
                tb.FontFamily = FluentPalette.MathSerifFont;
                tb.FontStyle = FontStyle.Italic;
                break;
            case MathTextStyle.Bold:
                tb.FontFamily = FluentPalette.MathSerifFont;
                tb.FontWeight = FontWeight.Bold;
                break;
            case MathTextStyle.BlackboardBold:
                tb.FontFamily = FluentPalette.MathSerifFont;
                tb.FontWeight = FontWeight.SemiBold;
                break;
            case MathTextStyle.Monospace:
                tb.FontFamily = FluentPalette.MonoFont;
                tb.FontSize = Math.Max(11, fontSize - 1);
                break;
            case MathTextStyle.Roman:
                tb.FontFamily = FluentPalette.MathSerifFont;
                tb.FontStyle = FontStyle.Normal;
                break;
            default:
                tb.FontFamily = FluentPalette.MathSerifFont;
                break;
        }

        if (txt.Text is "∑" or "∏" or "∐" or "∫" or "∬" or "∭" or "⨌" or "∮" or "∯" or "∰" or "⋃" or "⋂")
        {
            tb.FontSize = fontSize * 1.45;
            tb.Margin = new Thickness(1, -2, 2, -2);
        }

        return tb;
    }

    private static Control RenderFraction(MathFractionNode frac, double fontSize, bool isDisplay)
    {
        var childSize = Math.Max(9.5, fontSize * (isDisplay ? 0.86 : 0.72));
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(isDisplay ? 3 : 2, 0),
            Spacing = isDisplay ? 1.5 : 0
        };

        var num = RenderNode(frac.Numerator, childSize, isDisplay: false);
        num.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(num);

        if (frac.HasBar)
        {
            stack.Children.Add(new Border
            {
                Height = 1.0,
                MinWidth = isDisplay ? 14 : 10,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = FluentPalette.TextPrimary,
                Margin = new Thickness(0, isDisplay ? 1 : 0.5)
            });
        }

        var den = RenderNode(frac.Denominator, childSize, isDisplay: false);
        den.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(den);

        return stack;
    }

    private static Control RenderRoot(MathRootNode root, double fontSize, bool isDisplay)
    {
        var rootGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0)
        };

        if (root.Degree is not null)
        {
            var degCtrl = RenderNode(root.Degree, Math.Max(9.5, fontSize * 0.62), isDisplay: false);
            degCtrl.VerticalAlignment = VerticalAlignment.Top;
            degCtrl.Margin = new Thickness(0, 0, -4, 4);
            degCtrl[Grid.ColumnProperty] = 0;
            rootGrid.Children.Add(degCtrl);
        }

        var radicalGlyph = new PathShape
        {
            Data = Geometry.Parse("M 0,10 L 3,9 L 7,17 L 12,1"),
            Stroke = FluentPalette.TextPrimary,
            StrokeThickness = 1.35,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            Stretch = Stretch.Fill,
            Width = 11,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 1, 0, 0),
            [Grid.ColumnProperty] = 1
        };
        rootGrid.Children.Add(radicalGlyph);

        var bodyBorder = new Border
        {
            BorderBrush = FluentPalette.TextPrimary,
            BorderThickness = new Thickness(0, 1.2, 0, 0),
            Padding = new Thickness(3, 2, 3, 0),
            Margin = new Thickness(-0.5, 1, 0, 0),
            Child = RenderNode(root.Radicand, fontSize, isDisplay),
            [Grid.ColumnProperty] = 2
        };
        rootGrid.Children.Add(bodyBorder);

        return rootGrid;
    }

    private static Control RenderScript(MathScriptNode script, double fontSize, bool isDisplay)
    {
        var scriptSize = Math.Max(9.5, fontSize * 0.72);

        // Display-mode limits above/below sum, prod, lim, etc.
        if (isDisplay && LatexMathEngine.IsUnderscoreLimitOperator(script.Base))
        {
            var limitStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0),
                Spacing = 0
            };

            if (script.Superscript is not null)
            {
                var sup = RenderNode(script.Superscript, scriptSize, isDisplay: false);
                sup.HorizontalAlignment = HorizontalAlignment.Center;
                limitStack.Children.Add(sup);
            }

            var op = RenderNode(script.Base, fontSize, isDisplay: true);
            op.HorizontalAlignment = HorizontalAlignment.Center;
            limitStack.Children.Add(op);

            if (script.Subscript is not null)
            {
                var sub = RenderNode(script.Subscript, scriptSize, isDisplay: false);
                sub.HorizontalAlignment = HorizontalAlignment.Center;
                limitStack.Children.Add(sub);
            }

            return limitStack;
        }

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var baseCtrl = RenderNode(script.Base, fontSize, isDisplay);
        baseCtrl.VerticalAlignment = VerticalAlignment.Center;
        baseCtrl[Grid.ColumnProperty] = 0;
        row.Children.Add(baseCtrl);

        var scriptCol = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0.5, 0, 1, 0),
            Spacing = 0,
            [Grid.ColumnProperty] = 1
        };

        if (script.Superscript is not null)
        {
            var sup = RenderNode(script.Superscript, scriptSize, isDisplay: false);
            sup.Margin = new Thickness(0, -4, 0, script.Subscript is null ? 5 : 1);
            scriptCol.Children.Add(sup);
        }
        if (script.Subscript is not null)
        {
            var sub = RenderNode(script.Subscript, scriptSize, isDisplay: false);
            sub.Margin = new Thickness(0, script.Superscript is null ? 6 : 0, 0, -2);
            scriptCol.Children.Add(sub);
        }

        row.Children.Add(scriptCol);
        return row;
    }

    private static Control RenderAccent(MathAccentNode accent, double fontSize, bool isDisplay)
    {
        if (accent.AccentKind == "underline")
        {
            return new Border
            {
                BorderBrush = FluentPalette.TextPrimary,
                BorderThickness = new Thickness(0, 0, 0, 1.1),
                Padding = new Thickness(1, 0, 1, 1),
                Child = RenderNode(accent.Base, fontSize, isDisplay)
            };
        }

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 0
        };

        Control accentMark = accent.AccentKind switch
        {
            "vec" => new PathShape
            {
                Data = Geometry.Parse("M 0,3 L 12,3 M 9,0.5 L 12,3 L 9,5.5"),
                Stroke = FluentPalette.TextPrimary,
                StrokeThickness = 1.1,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                Height = 5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(1, 0, 1, -1)
            },
            "bar" => new Border
            {
                Height = 1.1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = FluentPalette.TextPrimary,
                Margin = new Thickness(1, 1, 1, 0)
            },
            "hat" => new TextBlock
            {
                Text = "ˆ",
                FontSize = Math.Max(10, fontSize * 0.85),
                FontFamily = FluentPalette.MathSerifFont,
                Foreground = FluentPalette.TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -4, 0, -6)
            },
            "tilde" => new TextBlock
            {
                Text = "~",
                FontSize = Math.Max(10, fontSize * 0.85),
                FontFamily = FluentPalette.MathSerifFont,
                Foreground = FluentPalette.TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -4, 0, -6)
            },
            "dot" => new TextBlock
            {
                Text = "˙",
                FontSize = Math.Max(10, fontSize * 0.9),
                Foreground = FluentPalette.TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -4, 0, -7)
            },
            "ddot" => new TextBlock
            {
                Text = "¨",
                FontSize = Math.Max(10, fontSize * 0.9),
                Foreground = FluentPalette.TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -4, 0, -7)
            },
            _ => new Border()
        };

        stack.Children.Add(accentMark);
        var baseCtrl = RenderNode(accent.Base, fontSize, isDisplay);
        baseCtrl.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(baseCtrl);
        return stack;
    }

    private static Control RenderDelimited(MathDelimitedNode delim, double fontSize, bool isDisplay)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1
        };

        var needsTall = LatexMathEngine.RequiresTwoDimensionalLayout(delim.Body);
        var delimSize = needsTall ? fontSize * 1.45 : fontSize;

        if (!string.IsNullOrEmpty(delim.Left))
        {
            panel.Children.Add(new TextBlock
            {
                Text = delim.Left,
                FontSize = delimSize,
                FontFamily = FluentPalette.MathSerifFont,
                Foreground = FluentPalette.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        panel.Children.Add(RenderNode(delim.Body, fontSize, isDisplay));

        if (!string.IsNullOrEmpty(delim.Right))
        {
            panel.Children.Add(new TextBlock
            {
                Text = delim.Right,
                FontSize = delimSize,
                FontFamily = FluentPalette.MathSerifFont,
                Foreground = FluentPalette.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return panel;
    }

    private static Control RenderMatrix(MathMatrixNode matrix, double fontSize, bool isDisplay)
    {
        var rowCount = matrix.Rows.Count;
        var colCount = matrix.Rows.Max(r => r.Count);
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };

        for (var c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < rowCount; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var r = 0; r < rowCount; r++)
        {
            for (var c = 0; c < matrix.Rows[r].Count; c++)
            {
                var cell = RenderNode(matrix.Rows[r][c], fontSize * (rowCount > 1 ? 0.92 : 1.0), isDisplay);
                cell.HorizontalAlignment = matrix.IsLeftAligned ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                cell.VerticalAlignment = VerticalAlignment.Center;
                cell.Margin = new Thickness(c == 0 ? 2 : 8, 3, c == colCount - 1 ? 2 : 8, 3);
                cell[Grid.RowProperty] = r;
                cell[Grid.ColumnProperty] = c;
                grid.Children.Add(cell);
            }
        }

        if (string.IsNullOrEmpty(matrix.LeftDelimiter) && string.IsNullOrEmpty(matrix.RightDelimiter))
            return grid;

        var wrapper = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2
        };

        var tallSize = Math.Max(fontSize * 1.3, fontSize * (0.95 + rowCount * 0.65));
        if (!string.IsNullOrEmpty(matrix.LeftDelimiter))
        {
            wrapper.Children.Add(new TextBlock
            {
                Text = matrix.LeftDelimiter,
                FontSize = tallSize,
                FontFamily = FluentPalette.MathSerifFont,
                Foreground = FluentPalette.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        wrapper.Children.Add(grid);

        if (!string.IsNullOrEmpty(matrix.RightDelimiter))
        {
            wrapper.Children.Add(new TextBlock
            {
                Text = matrix.RightDelimiter,
                FontSize = tallSize,
                FontFamily = FluentPalette.MathSerifFont,
                Foreground = FluentPalette.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return wrapper;
    }

    private static Control RenderStack(MathStackNode stackNode, double fontSize, bool isDisplay)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 0
        };
        var top = RenderNode(stackNode.Top, Math.Max(9.5, fontSize * 0.7), isDisplay: false);
        top.HorizontalAlignment = HorizontalAlignment.Center;
        top.Margin = new Thickness(0, 0, 0, -1);
        panel.Children.Add(top);

        var bottom = RenderNode(stackNode.Bottom, fontSize, isDisplay);
        bottom.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(bottom);
        return panel;
    }
}
