using System.Windows;
using GNSSR.Host.UI.ViewModels;
using Wpf.Ui.Controls;

namespace GNSSR.Host.UI;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        MainTabs.SelectedIndex = 0;

        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        await _viewModel.DisposeAsync();
    }

    private void NavigationTabButton_OnChecked(object sender, RoutedEventArgs e)
    {
        if (MainTabs is null)
        {
            return;
        }

        if (sender is not FrameworkElement element || element.Tag is null)
        {
            return;
        }

        if (!int.TryParse(element.Tag.ToString(), out var tabIndex))
        {
            return;
        }

        MainTabs.SelectedIndex = tabIndex;
    }
}
