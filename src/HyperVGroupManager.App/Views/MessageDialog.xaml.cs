using System.Windows;

namespace HyperVGroupManager.App.Views;

/// <summary>
/// Generischer Anzeige-Dialog, wiederverwendet für Fehlerdetails und das
/// Ergebnis eines Änderungslaufs.
/// </summary>
public partial class MessageDialog : Window
{
    public MessageDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }
}
