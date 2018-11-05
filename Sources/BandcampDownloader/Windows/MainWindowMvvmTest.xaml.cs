using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using BandcampDownloader.MediatorMessages;
using BandcampDownloader.ViewModels;
using Config.Net;
using DevExpress.Mvvm;
using ImageResizer;

namespace BandcampDownloader {

    public partial class MainWindowTest: Window {

        //TODO - Remove the view model hacks, these were just done for experimental purposes!
        #region Fields
        private Boolean userCancelled;

        #endregion Fields

        #region Trigger Methods
        /// <summary>
        /// Updates the state of the controls.
        /// </summary>
        /// <param name="downloadStarted">True if the download just started, false if it just stopped.</param>
        private void UpdateControlsState(bool downloadStarted)
        {
            this.Dispatcher.Invoke(new Action(() => {
                if (downloadStarted)
                {
                    // We just started the download
                    richTextBoxLog.Document.Blocks.Clear();
                    labelProgress.Content = "";
                    progressBar.IsIndeterminate = true;
                    progressBar.Value = progressBar.Minimum;
                    TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
                    TaskbarItemInfo.ProgressValue = 0;
                    buttonStart.IsEnabled = false;
                    buttonStop.IsEnabled = true;
                    buttonBrowse.IsEnabled = false;
                    textBoxUrls.IsReadOnly = true;
                    textBoxDownloadsLocation.IsReadOnly = true;
                    checkBoxCoverArtInFolder.IsEnabled = false;
                    checkBoxCoverArtInTags.IsEnabled = false;
                    checkBoxTag.IsEnabled = false;
                    checkBoxOneAlbumAtATime.IsEnabled = false;
                    checkBoxDownloadDiscography.IsEnabled = false;
                    checkBoxConvertToJpg.IsEnabled = false;
                    checkBoxResizeCoverArt.IsEnabled = false;
                    textBoxCoverArtMaxSize.IsEnabled = false;
                    checkBoxRetrieveFilesizes.IsEnabled = false;
                }
                else
                {
                    // We just finished the download (or user has cancelled)
                    buttonStart.IsEnabled = true;
                    buttonStop.IsEnabled = false;
                    buttonBrowse.IsEnabled = true;
                    textBoxUrls.IsReadOnly = false;
                    progressBar.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF01D328")); // Green
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = progressBar.Minimum;
                    TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
                    TaskbarItemInfo.ProgressValue = 0;
                    textBoxDownloadsLocation.IsReadOnly = false;
                    checkBoxCoverArtInFolder.IsEnabled = true;
                    checkBoxCoverArtInTags.IsEnabled = true;
                    checkBoxTag.IsEnabled = true;
                    checkBoxOneAlbumAtATime.IsEnabled = true;
                    checkBoxDownloadDiscography.IsEnabled = true;
                    labelDownloadSpeed.Content = "";
                    checkBoxConvertToJpg.IsEnabled = true;
                    checkBoxResizeCoverArt.IsEnabled = true;
                    textBoxCoverArtMaxSize.IsEnabled = true;
                    checkBoxRetrieveFilesizes.IsEnabled = true;
                }
            }));
        }


        /// <summary>
        /// Displays the specified message in the log with the specified color.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="color">The color.</param>
        private void Log(DownloaderLogMessage message)
        {
            if (!((MainViewModel)DataContext).UserSettings.ShowVerboseLog && (message.LogType == LogType.Warning || message.LogType == LogType.VerboseInfo))
            {
                return;
            }

            this.Dispatcher.Invoke(new Action(() => {
                // Time
                var textRange = new TextRange(richTextBoxLog.Document.ContentEnd, richTextBoxLog.Document.ContentEnd);
                textRange.Text = DateTime.Now.ToString("HH:mm:ss") + " ";
                textRange.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Gray);
                // Message
                textRange = new TextRange(richTextBoxLog.Document.ContentEnd, richTextBoxLog.Document.ContentEnd);
                textRange.Text = message.LogEntry;
                textRange.ApplyPropertyValue(TextElement.ForegroundProperty, LogHelper.GetColor(message.LogType));
                // Line break

                richTextBoxLog.AppendText(Environment.NewLine);
                if (((MainViewModel)DataContext).UserSettings.AutoScrollLog)
                {
                    richTextBoxLog.ScrollToEnd();
                }
            }));
        }
        #endregion

        #region Constructor

        public MainWindowTest(MainViewModel vm) {
            DataContext = vm;
            InitializeSettings(false);
            InitializeComponent();
            Messenger.Default.Register<DownloaderLogMessage>(this, Log);
        }

        #endregion Constructor

        #region Events

        private void buttonBrowse_Click(object sender, RoutedEventArgs e) {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select the folder to save albums";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                textBoxDownloadsLocation.Text = dialog.SelectedPath;
            }
        }

        private void buttonDefaultSettings_Click(object sender, RoutedEventArgs e) {
            if (MessageBox.Show("Reset settings to their default values?", "Bandcamp Downloader", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK) {
                InitializeSettings(true);
            }
        }

        private void buttonStart_Click(object sender, RoutedEventArgs e) {
            if (textBoxUrls.Text == Constants.UrlsHint) {
                // No URL to look
                Log(new DownloaderLogMessage("Paste some albums URLs to be downloaded", LogType.Error));

                return;
            }
            int coverArtMaxSize = 0;
            if (checkBoxResizeCoverArt.IsChecked.Value && !Int32.TryParse(textBoxCoverArtMaxSize.Text, out coverArtMaxSize))
            {
                Log(new DownloaderLogMessage("Cover art max width/height must be an integer", LogType.Error));
                return;
            }

            this.userCancelled = false;

            ((MainViewModel) DataContext).PendingDownloads = new ObservableCollection<WebClient>();

            // Set controls to "downloading..." state
            ((MainViewModel)DataContext).DownloadStarted = true;
            UpdateControlsState(true);

            Log(new DownloaderLogMessage("Starting download...", LogType.Info));

            // Get user inputs
            List<String> userUrls = textBoxUrls.Text.Split(new String[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
            userUrls = userUrls.Distinct().ToList();

            var urls = new List<String>();
            var albums = new List<Album>();

            Task.Factory.StartNew(() => {
                // Get URLs of albums to download
                if (((MainViewModel)DataContext).UserSettings.DownloadArtistDiscography) {
                    urls = ((MainViewModel)DataContext).GetArtistDiscography(userUrls);
                } else {
                    urls = userUrls;
                }
                urls = urls.Distinct().ToList();
            }).ContinueWith(x => {
                // Get info on albums
                albums = ((MainViewModel)DataContext).GetAlbums(urls);
            }).ContinueWith(x => {
                // Save files to download (we'll need the list to update the progressBar)
                ((MainViewModel)DataContext).FilesDownload = ((MainViewModel)DataContext).GetFilesToDownload(albums, ((MainViewModel)DataContext).UserSettings.SaveCoverArtInTags || ((MainViewModel)DataContext).UserSettings.SaveCoverArtInFolder);
            }).ContinueWith(x => {
                // Set progressBar max value
                long maxProgressBarValue;
                if (((MainViewModel)DataContext).UserSettings.RetrieveFilesizes) {
                    maxProgressBarValue = ((MainViewModel)DataContext).FilesDownload.Sum(f => f.Size); // Bytes to download
                } else {
                    maxProgressBarValue = ((MainViewModel)DataContext).FilesDownload.Count; // Number of files to download
                }
                if (maxProgressBarValue > 0) {
                    this.Dispatcher.Invoke(new Action(() => {
                        progressBar.IsIndeterminate = false;
                        progressBar.Maximum = maxProgressBarValue;
                        TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
                    }));
                }
            }).ContinueWith(x => {
                // Start downloading albums
                if (((MainViewModel)DataContext).UserSettings.DownloadOneAlbumAtATime) 
                {
                    // Download one album at a time

                    foreach (Album album in albums)
                    {
                        ((MainViewModel) DataContext).DownloadAlbum(album, ((MainViewModel)DataContext).ParseDownloadLocation(
                                ((MainViewModel)DataContext).UserSettings.DownloadsLocation, album),
                            ((MainViewModel) DataContext).UserSettings.TagTracks,
                            ((MainViewModel) DataContext).UserSettings.SaveCoverArtInTags,
                            ((MainViewModel) DataContext).UserSettings.SaveCoverArtInFolder,
                            ((MainViewModel) DataContext).UserSettings.ConvertCoverArtToJpg,
                            ((MainViewModel) DataContext).UserSettings.ResizeCoverArt,
                            ((MainViewModel) DataContext).UserSettings.CoverArtMaxSize);
                    }

                }
                else {
                    // Parallel download
                    Task[] tasks = new Task[albums.Count];
                    for (int i = 0; i < albums.Count; i++) {
                        Album album = albums[i]; // Mandatory or else => race condition
                        tasks[i] = Task.Factory.StartNew(() =>
                            ((MainViewModel) DataContext).DownloadAlbum(album,
                                ((MainViewModel) DataContext).ParseDownloadLocation(
                                    ((MainViewModel) DataContext).UserSettings.DownloadsLocation, album),
                                ((MainViewModel) DataContext).UserSettings.TagTracks,
                                ((MainViewModel) DataContext).UserSettings.SaveCoverArtInTags,
                                ((MainViewModel) DataContext).UserSettings.SaveCoverArtInFolder,
                                ((MainViewModel) DataContext).UserSettings.ConvertCoverArtToJpg,
                                ((MainViewModel) DataContext).UserSettings.ResizeCoverArt,
                                ((MainViewModel) DataContext).UserSettings.CoverArtMaxSize));

                    }
                    // Wait for all albums to be downloaded
                    Task.WaitAll(tasks);
                }
            }).ContinueWith(x => {
                if (this.userCancelled) {
                    // Display message if user cancelled
                    Log(new DownloaderLogMessage("Downloads cancelled by user", LogType.Info));
                }
                // Set controls to "ready" state
                ((MainViewModel)DataContext).ActiveDownloads = false;
                UpdateControlsState(false);
                // Play a sound
                try {
                    ( new SoundPlayer(@"C:\Windows\Media\Windows Ding.wav") ).Play();
                } catch {
                }
            });
        }

        private void buttonStop_Click(object sender, RoutedEventArgs e) {
            if (MessageBox.Show("Would you like to cancel all downloads?", "Bandcamp Downloader", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) {
                return;
            }

            this.userCancelled = true;
            Cursor = Cursors.Wait;
            Log(new DownloaderLogMessage("Cancelling downloads. Please wait...", LogType.Info));

            lock (((MainViewModel)DataContext).PendingDownloads) {
                if (((MainViewModel)DataContext).PendingDownloads.Count == 0) {
                    // Nothing to cancel
                    Cursor = Cursors.Arrow;
                    return;
                }
            }

            buttonStop.IsEnabled = false;
            progressBar.Foreground = System.Windows.Media.Brushes.Red;
            progressBar.IsIndeterminate = true;
            TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
            TaskbarItemInfo.ProgressValue = 0;

            lock (((MainViewModel)DataContext).PendingDownloads) {
                // Stop current downloads
                foreach (WebClient webClient in ((MainViewModel)DataContext).PendingDownloads) {
                    webClient.CancelAsync();
                }
            }

            Cursor = Cursors.Arrow;
        }

        private void InitializeSettings(Boolean resetToDefaults) {
            if (resetToDefaults) {
                File.Delete(Constants.UserSettingsFilePath);
            }
            // Must set this before UI forms, cannot default in settings as it isn't determined by a constant function
            if (String.IsNullOrEmpty(((MainViewModel)DataContext).UserSettings.DownloadsLocation)) {
                ((MainViewModel)DataContext).UserSettings.DownloadsLocation = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\{artist}\\{album}";
            }
            ((MainViewModel)DataContext).UserSettings = new ConfigurationBuilder<UserSettings>().UseIniFile(Constants.UserSettingsFilePath).Build();
        }

        private void labelVersion_MouseDown(object sender, MouseButtonEventArgs e) {
            Process.Start(Constants.ProjectWebsite);
        }

        private void textBoxUrls_GotFocus(object sender, RoutedEventArgs e) {
            if (textBoxUrls.Text == Constants.UrlsHint) {
                // Erase the hint message
                textBoxUrls.Text = "";
                textBoxUrls.Foreground = new SolidColorBrush(Colors.Black);
            }
        }

        private void textBoxUrls_LostFocus(object sender, RoutedEventArgs e) {
            if (textBoxUrls.Text == "") {
                // Show the hint message
                textBoxUrls.Text = Constants.UrlsHint;
                textBoxUrls.Foreground = new SolidColorBrush(Colors.DarkGray);
            }
        }

        private void WindowMain_Closing(object sender, CancelEventArgs e) {
            if (((MainViewModel)DataContext).DownloadStarted) {
                // There are active downloads, ask for confirmation
                if (MessageBox.Show("There are currently active downloads. Are you sure you want to close the application and stop all downloads?", "Bandcamp Downloader", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.Cancel) {
                    // Cancel closing the window
                    e.Cancel = true;
                }
            }
        }

        #endregion Events
    }
}