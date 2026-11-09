using System.Windows;
using CleanC.App.ViewModels;

namespace CleanC.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OpenWeChat_Click(object sender, RoutedEventArgs e) => new WeChatReadWindow { Owner = this }.Show();
    private void OpenDisk_Click(object sender, RoutedEventArgs e) => new DiskWindow { Owner = this }.Show();
    private void OpenQq_Click(object sender, RoutedEventArgs e) => new QqReadWindow { Owner = this }.Show();

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await _viewModel.ScanAsync();
    }
}
