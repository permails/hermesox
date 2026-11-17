using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace HermesDesktop.App.Controls;

internal static class FluentPalette
{
    public static bool IsLight => Application.Current?.ActualThemeVariant == ThemeVariant.Light;

    public static IBrush TextPrimary => Brush.Parse(IsLight ? "#E4000000" : "#FFFFFFFF");
    public static IBrush TextSecondary => Brush.Parse(IsLight ? "#9E000000" : "#C5C5C5");
    public static IBrush TextMuted => Brush.Parse(IsLight ? "#72000000" : "#8E8E8E");
    public static IBrush AccentText => Brush.Parse(IsLight ? "#003E92" : "#99EBFF");
    public static IBrush AccentFill => Brush.Parse(IsLight ? "#005FB8" : "#60CDFF");
    public static IBrush AccentForeground => Brush.Parse(IsLight ? "#FFFFFF" : "#000000");
    public static IBrush AccentSoft => Brush.Parse(IsLight ? "#14005FB8" : "#1F60CDFF");
    public static IBrush AccentStroke => Brush.Parse(IsLight ? "#33005FB8" : "#4D60CDFF");
    public static IBrush CardFill => Brush.Parse(IsLight ? "#FFFFFF" : "#2D2D2D");
    public static IBrush CardSecondary => Brush.Parse(IsLight ? "#F7F7F7" : "#242424");
    public static IBrush CardStroke => Brush.Parse(IsLight ? "#0F000000" : "#14FFFFFF");
    public static IBrush ControlFill => Brush.Parse(IsLight ? "#FFFFFF" : "#0FFFFFFF");
    public static IBrush ControlStroke => Brush.Parse(IsLight ? "#1E000000" : "#18FFFFFF");
    public static IBrush CodeBack => Brush.Parse(IsLight ? "#F4F4F4" : "#1C1C1C");
    public static IBrush InlineCodeBack => Brush.Parse(IsLight ? "#0D000000" : "#18FFFFFF");
    public static IBrush InlineCodeText => Brush.Parse(IsLight ? "#003E92" : "#99EBFF");
    public static IBrush MathText => Brush.Parse(IsLight ? "#005FB8" : "#99EBFF");
    public static IBrush LinkText => Brush.Parse(IsLight ? "#005FB8" : "#60CDFF");
    public static IBrush TableHeaderBack => Brush.Parse(IsLight ? "#F3F3F3" : "#242424");
    public static IBrush TableRowEven => Brush.Parse(IsLight ? "#FFFFFF" : "#2D2D2D");
    public static IBrush TableRowOdd => Brush.Parse(IsLight ? "#FAFAFA" : "#272727");
    public static IBrush TableBorder => Brush.Parse(IsLight ? "#12000000" : "#14FFFFFF");

    // Syntax highlighting (VS Code Light+ / Dark+ aligned with Fluent 2)
    public static IBrush SyntaxKeyword => Brush.Parse(IsLight ? "#0000FF" : "#569CD6");
    public static IBrush SyntaxString => Brush.Parse(IsLight ? "#A31515" : "#CE9178");
    public static IBrush SyntaxComment => Brush.Parse(IsLight ? "#008000" : "#6A9955");
    public static IBrush SyntaxNumber => Brush.Parse(IsLight ? "#098658" : "#B5CEA8");

    public static readonly FontFamily MathSerifFont = new("Cambria Math, STIX Two Math, Times New Roman, Georgia, serif");
    public static readonly FontFamily MonoFont = new("Cascadia Code, Cascadia Mono, Consolas, Menlo, monospace");
}
