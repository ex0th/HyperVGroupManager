using System.Windows;

namespace HyperVGroupManager.App.Views;

/// <summary>
/// Zeigt Name und Mitgliederzahl an. Bei einer nicht leeren Gruppe ist nur "Schließen"
/// verfügbar - das Löschen selbst (inkl. Regelprüfung) übernimmt MainViewModel.DeleteGroupCommand.
/// </summary>
public partial class DeleteGroupConfirmationDialog : Window
{
    public DeleteGroupConfirmationDialog(string groupName, int memberCount)
    {
        InitializeComponent();

        if (memberCount > 0)
        {
            ConfirmationPanel.Visibility = Visibility.Collapsed;
            BlockedPanel.Visibility = Visibility.Visible;
            BlockedGroupNameText.Text = groupName;
            MemberCountText.Text = memberCount.ToString();
            ConfirmButton.Visibility = Visibility.Collapsed;
            CloseButton.Content = "Schließen";
        }
        else
        {
            ConfirmGroupNameText.Text = groupName;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
