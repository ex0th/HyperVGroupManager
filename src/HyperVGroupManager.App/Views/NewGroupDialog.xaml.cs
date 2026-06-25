using System.Windows;

namespace HyperVGroupManager.App.Views;

/// <summary>
/// Sammelt nur die Eingabe (Gruppenname); Validierung und Anlegen erfolgen über
/// MainViewModel.CreateGroupCommand, nicht in diesem Code-Behind.
/// </summary>
public partial class NewGroupDialog : Window
{
    public string GroupName { get; private set; } = string.Empty;

    public NewGroupDialog(string suggestedPrefix)
    {
        InitializeComponent();
        GroupNameTextBox.Text = suggestedPrefix;
        Loaded += (_, _) =>
        {
            GroupNameTextBox.Focus();
            GroupNameTextBox.SelectAll();
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        GroupName = GroupNameTextBox.Text;
        DialogResult = true;
    }
}
