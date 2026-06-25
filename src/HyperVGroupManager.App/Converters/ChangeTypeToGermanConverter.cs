using System.Globalization;
using System.Windows.Data;
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
            VmGroupChangeType.AddMembership => "Mitglied hinzufügen",
            VmGroupChangeType.RemoveMembership => "Mitglied entfernen",
            VmGroupChangeType.CreateGroup => "Gruppe erstellen",
            VmGroupChangeType.RenameGroup => "Gruppe umbenennen",
            VmGroupChangeType.DeleteGroup => "Gruppe löschen",
            _ => changeType.ToString(),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
