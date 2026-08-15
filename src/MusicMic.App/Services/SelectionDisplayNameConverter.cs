using System.Globalization;
using System.Windows.Data;

namespace MusicMic.App.Services;

public sealed class SelectionDisplayNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        object? displayName = value.GetType().GetProperty("DisplayName")?.GetValue(value);
        return displayName as string ?? value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
