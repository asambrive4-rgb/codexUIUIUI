using System.Windows;
using CodexSwitcher.Bootstrapper.Presentation;

namespace CodexSwitcher.Bootstrapper;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

