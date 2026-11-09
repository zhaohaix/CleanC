using System.Windows;

namespace CleanC.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                args.Exception.Message,
                "CleanC 发生错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
