using System.Windows;

namespace HyperVGroupManager.App.Views;

public partial class RenameGroupDialog : Window
{
    public string NewName { get; private set; } = string.Empty;

    public RenameGroupDialog(string currentName)
    {
        InitializeComponent();
        CurrentNameText.Text = $"Neuer Name für '{currentName}':";
        NewNameTextBox.Text = currentName;
        Loaded += (_, _) =>
        {
            NewNameTextBox.Focus();
            NewNameTextBox.SelectAll();
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        NewName = NewNameTextBox.Text;
        DialogResult = true;
    }
}
