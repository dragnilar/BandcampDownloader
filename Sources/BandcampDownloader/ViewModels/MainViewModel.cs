using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using BandcampDownloader.Annotations;
using BandcampDownloader.MediatorMessages;
using BandcampDownloader.ProgressReporters;
using BandcampDownloader.Services;
using Config.Net;
using DevExpress.Mvvm;
using ImageResizer;
using TagLib;
using File = System.IO.File;

namespace BandcampDownloader.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public MainViewModel()
        {
            ServicePointManager.DefaultConnectionLimit = 50;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            versionText = "v" + Assembly.GetEntryAssembly().GetName().Version;
            Task.Factory.StartNew(CheckForUpdates);
            StartDownloadCommand = new DelegateCommand(StartDownload);
        }

        private DownloadService Downloader = new DownloadService();

        public ICommand StartDownloadCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        #region INotifyPropertyChanged Fields

        private long progressBarValue;

        public long ProgressBarValue
        {
            get => progressBarValue;
            set
            {
                if (value == progressBarValue) return;
                progressBarValue = value;
                OnPropertyChanged(nameof(ProgressBarValue));
            }
        }

        private string versionText;

        public string VersionText
        {
            get => versionText;
            set
            {
                if (value == versionText) return;
                versionText = value;
                OnPropertyChanged(nameof(VersionText));
            }
        }

        private bool activeDownloads;

        public bool ActiveDownloads
        {
            get => activeDownloads;
            set
            {
                if (value == activeDownloads) return;
                activeDownloads = value;
                OnPropertyChanged(nameof(ActiveDownloads));
            }
        }

        private DateTime lastDownloadSpeedUpdate;

        public DateTime LastDownloadSpeedUpdate
        {
            get => lastDownloadSpeedUpdate;
            set
            {
                if (value.Equals(lastDownloadSpeedUpdate)) return;
                lastDownloadSpeedUpdate = value;
                OnPropertyChanged(nameof(LastDownloadSpeedUpdate));
            }
        }


        private long lastTotalReceivedBytes;

        public long LastTotalReceivedBytes
        {
            get => lastTotalReceivedBytes;
            set
            {
                if (value == lastTotalReceivedBytes) return;
                lastTotalReceivedBytes = value;
                OnPropertyChanged(nameof(LastTotalReceivedBytes));
            }
        }

        private long maximumProgressValue;
        private long progressValue;
        private bool progressIndeterminate;
        private string progressString;


        public long MaximumProgressValue
        {
            get => maximumProgressValue;
            set
            {
                if (value == maximumProgressValue) return;
                maximumProgressValue = value;
                OnPropertyChanged(nameof(MaximumProgressValue));
            }
        }

        public long ProgressValue
        {
            get => progressValue;
            set
            {
                if (value == progressValue) return;
                progressValue = value;
                OnPropertyChanged(nameof(ProgressValue));
            }
        }

        public bool ProgressIndeterminate
        {
            get => progressIndeterminate;
            set
            {
                if (value == progressIndeterminate) return;
                progressIndeterminate = value;
                OnPropertyChanged(nameof(ProgressIndeterminate));
            }
        }

        public string ProgressString
        {
            get => progressString;
            set
            {
                if (value == progressString) return;
                progressString = value;
                OnPropertyChanged(nameof(ProgressString));
            }
        }


        private bool userCanceled;


        public bool UserCanceled
        {
            get => userCanceled;
            set
            {
                if (value == userCanceled) return;
                userCanceled = value;
                OnPropertyChanged(nameof(UserCanceled));
            }
        }

        private string downloadSpeedString;

        public string DownloadSpeedString
        {
            get => downloadSpeedString;
            set
            {
                if (value == downloadSpeedString) return;
                downloadSpeedString = value;
                OnPropertyChanged(nameof(DownloadSpeedString));
            }
        }

        private IUserSettings userSettings = Globals.UserSettings;


        public IUserSettings UserSettings
        {
            get => userSettings;
            set
            {
                if (value == userSettings) return;

                userSettings = value;
                OnPropertyChanged(nameof(UserSettings));
            }
        }

        private string logString; //TODO - Replace with something more robust

        public string LogString
        {
            get => logString;
            set
            {
                if (value == logString) return;
                logString = value;
                OnPropertyChanged(nameof(LogString));
            }
        }

        private string _urls;

        public string Urls
        {
            get => _urls;
            set
            {
                if (value == _urls) return;
                _urls = value;
                OnPropertyChanged(nameof(Urls));
            }
        }

        public ObservableCollection<WebClient> PendingDownloads;

        #endregion

        #region Methods

        /// <summary>
        ///     Displays a message if a new version is available.
        /// </summary>
        private void CheckForUpdates()
        {
            // Note: GitHub uses a HTTP redirect to redirect from the generic latest release page to the actual latest release page
            var failedToRetrieveLatestVersion = false;

            // Retrieve the redirect page from the GitHub latest release page
            var request = WebRequest.CreateHttp(Constants.LatestReleaseWebsite);
            request.AllowAutoRedirect = false;
            var redirectPage = "";
            try
            {
                using (var response = (HttpWebResponse) request.GetResponse())
                {
                    redirectPage = response.GetResponseHeader("Location");
                    // redirectPage should be like "https://github.com/Otiel/BandcampDownloader/releases/tag/vX.X.X.X"
                }
            }
            catch
            {
                failedToRetrieveLatestVersion = true;
            }

            // Extract the version number from the URL
            var latestVersionNumber = "";
            try
            {
                latestVersionNumber = redirectPage.Substring(redirectPage.LastIndexOf("/v") + 2); // X.X.X.X
            }
            catch
            {
                failedToRetrieveLatestVersion = true;
            }

            Version latestVersion;
            if (Version.TryParse(latestVersionNumber, out latestVersion))
            {
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion.CompareTo(latestVersion) < 0)
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        versionText += " - A new version is available";
                    }));
            }
            else
            {
                failedToRetrieveLatestVersion = true;
            }

            if (failedToRetrieveLatestVersion)
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    versionText += " - Could not check for updates";
                }));
        }



        private void Log(string eventMessage, LogType logType)
        {
            Messenger.Default.Send(new DownloaderLogMessage(eventMessage, logType));
        }

        #endregion

        #region Commands

        private void StartDownload()
        {
            if (Urls == Constants.UrlsHint)
            {
                // No URL to look
                Log("Paste some albums URLs to be downloaded", LogType.Error);

                return;
            }

            Downloader.PendingDownloads = PendingDownloads;
            Downloader.Urls = Urls;
            activeDownloads = Downloader.ActiveDownloads;
            downloadSpeedString = Downloader.DownloadSpeedString;
            
            Progress<DownloadProgressReporter> progress = new Progress<DownloadProgressReporter>(reporter =>
                {
                    progressValue = reporter.Progress;
                    progressString = reporter.Text;
                    progressBarValue = reporter.ProgressBarValue;
                });

            Downloader.StartDownload(progress);

        }




        #endregion
    }
}