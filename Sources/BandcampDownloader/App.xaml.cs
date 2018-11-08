using System.Linq;
using System.Windows;
using BandcampDownloader.ViewModels;
using DevExpress.Xpf.Core;

namespace BandcampDownloader
{
    /// <summary>
    ///     Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            RemoveUnusedThemes();
            var mainViewModel = new MainViewModel();
            Current.MainWindow = new MainWindow(mainViewModel);
            Current.MainWindow.Show();
        }

        private void RemoveUnusedThemes()
        {
            foreach (var theme in Theme.Themes.ToList())
            {
                switch (theme.Name)
                {
                    case Theme.Office2016BlackName:
                    case Theme.VS2017LightName:
                    case Theme.VS2017BlueName:
                    case Theme.VS2017DarkName:
                        continue;
                    default:
                        Theme.RemoveTheme(theme.Name);
                        break;
                }
            }
        }
    }
}