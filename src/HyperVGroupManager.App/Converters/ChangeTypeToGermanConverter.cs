using System.Globalization;
using System.Windows.Data;
using HyperVGroupManager.App.Localization;
using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.App.Converters;

public sealed class ChangeTypeToGermanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not VmGroupChangeType changeType)
        {
            return value?.ToString() ?? string.Empty;
        }

        return changeType switch
        {
            VmGroupChangeType.AddMembership => LocalizationService.Instance.Get("Change.AddMembership"),
            VmGroupChangeType.RemoveMembership => LocalizationService.Instance.Get("Change.RemoveMembership"),
            VmGroupChangeType.CreateGroup => LocalizationService.Instance.Get("Change.CreateGroup"),
            VmGroupChangeType.RenameGroup => LocalizationService.Instance.Get("Change.RenameGroup"),
            VmGroupChangeType.DeleteGroup => LocalizationService.Instance.Get("Change.DeleteGroup"),
            _ => changeType.ToString(),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
