using System.Windows;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace CodexSwitcher.Bootstrapper;

public partial class DeleteProfileConfirmationWindow : FluentWindow
{
    public DeleteProfileConfirmationWindow(string profileName)
    {
        InitializeComponent();
        TitleText = $"'{profileName}' 프로필을 삭제하시겠습니까?";
        DataContext = this;
    }

    public string TitleText { get; }

    private void Delete_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
