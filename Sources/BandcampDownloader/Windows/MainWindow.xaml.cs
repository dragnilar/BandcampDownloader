using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using BandcampDownloader.MediatorMessages;
using BandcampDownloader.ViewModels;
using Config.Net;
using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using Cursors = System.Windows.Input.Cursors;

namespace BandcampDownloader
{
    public partial class MainWindow : DevExpress.Xpf.Core.ThemedWindow
    {
        //TODO - Remove the view model hacks, these were just done for experimental purposes!


        #region Constructor

        public MainWindow(MainViewModel vm)
        {
            DataContext = vm;
            InitializeComponent();
            RegisterMediatorMessages();
        }

        private void RegisterMediatorMessages()
        {
            Messenger.Default.Register<DownloaderLogMessage>(this, ProcessAndDisplayLogMessage);
            Messenger.Default.Register<UpdateMainViewControlStateMessage>(this, ProcessControlStateMessage);
            Messenger.Default.Register<PlayASoundMessage>(this, ProcessPlayASoundMessage);
            Messenger.Default.Register<ProgressUpdateMessage>(this, ProcessProgressBarMessage);
        }

        #endregion Constructor

        #region Trigger Methods

        private void ProcessControlStateMessage(UpdateMainViewControlStateMessage message)
        {
            UpdateControlsState(message.DownloadStarted);
        }

        /// <summary>
        ///     Updates the state of the controls.
        /// </summary>
        /// <param name="downloadStarted">True if the download just started, false if it just stopped.</param>
        private void UpdateControlsState(bool downloadStarted)
        {
            Dispatcher.Invoke(() =>
            {
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
                    progressBar.Foreground =
                        new SolidColorBrush((Color) ColorConverter.ConvertFromString("#FF01D328")); // Green
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
            });
        }


        /// <summary>
        ///     Processes and displays the specified message in the log with the specified color.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="color">The color.</param>
        private void ProcessAndDisplayLogMessage(DownloaderLogMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                if (!Globals.UserSettings.ShowVerboseLog &&
                    (message.LogType == LogType.Warning || message.LogType == LogType.VerboseInfo)) return;

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
                if (Globals.UserSettings.AutoScrollLog) richTextBoxLog.ScrollToEnd();
            });
        }

        private void ProcessPlayASoundMessage(PlayASoundMessage message)
        {
            PlayASound();
        }

        private void PlayASound()
        {
            new SoundPlayer(@"C:\Windows\Media\Windows Ding.wav").Play();
        }

        private void ProcessProgressBarMessage(ProgressUpdateMessage message)
        {
            progressBar.IsIndeterminate = message.IsIndeterminate;
            progressBar.Maximum = message.Maximum;
            TaskbarItemInfo.ProgressState = message.ProgressState;
        }

        #endregion


        #region Events

        private void buttonBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderBrowserDialog {Description = "Select the folder to save albums"};
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                textBoxDownloadsLocation.Text = dialog.SelectedPath;
        }

        private void buttonDefaultSettings_Click(object sender, RoutedEventArgs e)
        {
            if (DXMessageBox.Show("Reset settings to their default values?", "Bandcamp Downloader",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) ==
                MessageBoxResult.OK) Globals.InitializeSettings(true);
        }

        private void buttonStop_Click(object sender, RoutedEventArgs e)
        {
            if (DXMessageBox.Show("Would you like to cancel all downloads?", "Bandcamp Downloader",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) !=
                MessageBoxResult.Yes) return;

            Dispatcher.Invoke(() =>
            {
                ((MainViewModel) DataContext).UserCanceled = true;
                Cursor = Cursors.Wait;
                ProcessAndDisplayLogMessage(new DownloaderLogMessage("Cancelling downloads. Please wait...",
                    LogType.Info));

                lock (((MainViewModel) DataContext).PendingDownloads)
                {
                    if (((MainViewModel) DataContext).PendingDownloads.Count == 0)
                    {
                        // Nothing to cancel
                        Cursor = Cursors.Arrow;
                        return;
                    }
                }

                buttonStop.IsEnabled = false;
                progressBar.Foreground = Brushes.Red;
                progressBar.IsIndeterminate = true;
                TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
                TaskbarItemInfo.ProgressValue = 0;

                lock (((MainViewModel) DataContext).PendingDownloads)
                {
                    // Stop current downloads
                    foreach (var webClient in ((MainViewModel) DataContext).PendingDownloads) webClient.CancelAsync();
                }

                Cursor = Cursors.Arrow;
            });
        }

        private void labelVersion_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Process.Start(Constants.ProjectWebsite);
        }

        private void textBoxUrls_GotFocus(object sender, RoutedEventArgs e)
        {
            if (textBoxUrls.Text == Constants.UrlsHint)
            {
                // Erase the hint message
                textBoxUrls.Text = "";
                textBoxUrls.Foreground = new SolidColorBrush(Colors.Black);
            }
        }

        private void textBoxUrls_LostFocus(object sender, RoutedEventArgs e)
        {
            if (textBoxUrls.Text == "")
            {
                // Show the hint message
                textBoxUrls.Text = Constants.UrlsHint;
                textBoxUrls.Foreground = new SolidColorBrush(Colors.DarkGray);
            }
        }

        private void WindowMain_Closing(object sender, CancelEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (((MainViewModel) DataContext).ActiveDownloads)
                    if (DXMessageBox.Show(
                            "There are currently active downloads. Are you sure you want to close the application and stop all downloads?",
                            "Bandcamp Downloader", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                            MessageBoxResult.Cancel) == MessageBoxResult.Cancel)
                        e.Cancel = true;
            });
        }

        #endregion Events
    }
}