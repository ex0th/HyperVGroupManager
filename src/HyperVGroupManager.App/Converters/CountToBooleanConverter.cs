using System.Globalization;
using System.Windows.Data;

namespace HyperVGroupManager.App.Converters;

/// <summary>Liefert true, wenn eine Anzahl/Auflistung mindestens ein Element enthält.</summary>
public sealed class CountToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        int count => count > 0,
        System.Collections.ICollection collection => collection.Count > 0,
        _ => false,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
