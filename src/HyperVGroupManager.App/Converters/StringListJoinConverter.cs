using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace HyperVGroupManager.App.Converters;

public sealed class StringListJoinConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is IEnumerable enumerable
            ? string.Join(", ", enumerable.Cast<object>())
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
