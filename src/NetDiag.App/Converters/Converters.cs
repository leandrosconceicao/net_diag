using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NetDiag.App.ViewModels;
using NetDiag.Core.Model;

namespace NetDiag.App.Converters;

internal static class Palette
{
    public static readonly SolidColorBrush Ok = Make(0x16, 0xA3, 0x4A);
    public static readonly SolidColorBrush Warning = Make(0xD9, 0x77, 0x06);
    public static readonly SolidColorBrush Error = Make(0xDC, 0x26, 0x26);
    public static readonly SolidColorBrush Info = Make(0x25, 0x63, 0xEB);
    public static readonly SolidColorBrush Neutral = Make(0x9A, 0xA5, 0xB1);

    private static SolidColorBrush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StepStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        StepState.Ok => Palette.Ok,
        StepState.Warning => Palette.Warning,
        StepState.Error => Palette.Error,
        StepState.Running => Palette.Info,
        _ => Palette.Neutral
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StepStateToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        StepState.Ok => "✔",
        StepState.Warning => "⚠",
        StepState.Error => "✖",
        StepState.Running => "◉",
        StepState.Skipped => "–",
        _ => "○"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class SeverityToBrushConverter : IValueConverter
{
    /// <summary>No veredito, "informação" significa tudo bem (verde); nos cartões, azul.</summary>
    public bool InfoIsGood { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        Severity.Critical => Palette.Error,
        Severity.Warning => Palette.Warning,
        _ => InfoIsGood ? Palette.Ok : Palette.Info
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
