using System.Windows;
using BandcampDownloader.ViewModels;

namespace BandcampDownloader {

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App: Application {
        protected override void OnStartup(StartupEventArgs e)
        {
            //
            var mainViewModel = new MainViewModel();
            Current.MainWindow = new MainWindow(mainViewModel);
            Current.MainWindow.Show();
        }
    }
}