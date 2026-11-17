using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using HermesDesktop.App.ViewModels;
using HermesDesktop.Core.Localization;
using HermesDesktop.Core.Models;

namespace HermesDesktop.App.Converters;

public sealed class TabLabelConverter : IValueConverter
{
    public static readonly TabLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not V2Tab tab) return string.Empty;
        return AppLocalization.TabLabel(tab);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class TabIconDataConverter : IValueConverter
{
    public static readonly TabIconDataConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not V2Tab tab) return null;
        var pathData = tab switch
        {
            V2Tab.Bots => "M12 2a4 4 0 0 1 4 4v1h1a3 3 0 0 1 3 3v6a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4v-6a3 3 0 0 1 3-3h1V6a4 4 0 0 1 4-4zm-2.5 9a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3zm5 0a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3z",
            V2Tab.Chats => "M4 5a3 3 0 0 1 3-3h10a3 3 0 0 1 3 3v8a3 3 0 0 1-3 3H9l-4.5 3.5A.5.5 0 0 1 3.7 19V16A3 3 0 0 1 4 13V5z",
            V2Tab.Tasks => "M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zm3 5l1.5 1.5L13 7m-5 6l1.5 1.5L13 11",
            V2Tab.Pulse => "M3 12h4l2.5-6 4 12 2.5-6H21",
            V2Tab.Artifacts => "M4 4h7v7H4V4zm9 0h7v7h-7V4zM4 13h7v7H4v-7zm9 0h7v7h-7v-7z",
            V2Tab.Settings => "M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7zm8.5 3.5a1 1 0 0 1-.8 1l-1.6.3a6.5 6.5 0 0 1-.7 1.7l.9 1.4a1 1 0 0 1-.1 1.2l-1.1 1.1a1 1 0 0 1-1.2.1l-1.4-.9a6.5 6.5 0 0 1-1.7.7l-.3 1.6a1 1 0 0 1-1 .8h-1.6a1 1 0 0 1-1-.8l-.3-1.6a6.5 6.5 0 0 1-1.7-.7l-1.4.9a1 1 0 0 1-1.2-.1l-1.1-1.1a1 1 0 0 1-.1-1.2l.9-1.4a6.5 6.5 0 0 1-.7-1.7l-1.6-.3a1 1 0 0 1-.8-1v-1.6a1 1 0 0 1 .8-1l1.6-.3a6.5 6.5 0 0 1 .7-1.7l-.9-1.4a1 1 0 0 1 .1-1.2l1.1-1.1a1 1 0 0 1 1.2-.1l1.4.9a6.5 6.5 0 0 1 1.7-.7l.3-1.6a1 1 0 0 1 1-.8h1.6a1 1 0 0 1 1 .8l.3 1.6a6.5 6.5 0 0 1 1.7.7l1.4-.9a1 1 0 0 1 1.2.1l1.1 1.1a1 1 0 0 1 .1 1.2l-.9 1.4a6.5 6.5 0 0 1 .7 1.7l1.6.3a1 1 0 0 1 .8 1V12z",
            _ => "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16z"
        };
        return Geometry.Parse(pathData);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ClarifyChoiceCommandParameterConverter : IMultiValueConverter
{
    public static readonly ClarifyChoiceCommandParameterConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 2 && values[0] is MessageItemViewModel msg && values[1] is ClarifyChoice choice)
        {
            return Tuple.Create(msg, choice);
        }
        return null;
    }
}

public sealed class IntGreaterThanZeroConverter : IValueConverter
{
    public static readonly IntGreaterThanZeroConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToAccentBrushConverter : IValueConverter
{
    public static readonly BoolToAccentBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        if (value is true) return Brush.Parse(isLight ? "#0A000000" : "#12FFFFFF");
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToBorderBrushConverter : IValueConverter
{
    public static readonly BoolToBorderBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        if (value is true) return Brush.Parse(isLight ? "#12000000" : "#16FFFFFF");
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
