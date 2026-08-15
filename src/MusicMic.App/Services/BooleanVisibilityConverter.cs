using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MusicMic.App.Services;

/// <summary>Maps a boolean to <see cref="Visibility"/> so the flyout can swap the accent
/// "Start" button for the standard "Stop" button without restyling a single control.</summary>
public sealed class BooleanVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool boolean && boolean;
        return flag != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
