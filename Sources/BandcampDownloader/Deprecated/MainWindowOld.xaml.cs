using System;
using System.Collections.Generic;
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
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using BandcampDownloader.ViewModels;
using Config.Net;
using ImageResizer;
using TagLib;
using Cursors = System.Windows.Input.Cursors;
using File = System.IO.File;
using MessageBox = System.Windows.MessageBox;

namespace BandcampDownloader.Deprecated
{
    public partial class MainWindowOld : Window
    {
        #region Constructor

        public MainWindowOld()
        {
            InitializeSettings(false);
            InitializeComponent();
            DataContext = userSettings;

            // Increase the maximum of concurrent connections to be able to download more than 2 (which is the default value) files at the
            // same time
            ServicePointManager.DefaultConnectionLimit = 50;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            // Hints
            textBoxUrls.Text = Constants.UrlsHint;
            textBoxUrls.Foreground = new SolidColorBrush(Colors.DarkGray);
            // Version
            labelVersion.Content = "v" + Assembly.GetEntryAssembly().GetName().Version;
            // Check for updates
            Task.Factory.StartNew(() => { CheckForUpdates(); });
        }

        #endregion Constructor

        #region Fields

        public IUserSettings userSettings =
            new ConfigurationBuilder<IUserSettings>().UseIniFile(Constants.UserSettingsFilePath).Build();

        /// <summary>
        ///     Indicates if there are active downloads
        /// </summary>
        private bool activeDownloads;

        /// <summary>
        ///     The files to download, or being downloaded, or downloaded. Used to compute the current received bytes and the total
        ///     bytes to
        ///     download.
        /// </summary>
        private List<TrackFile> filesDownload;

        /// <summary>
        ///     Used to compute and display the download speed.
        /// </summary>
        private DateTime lastDownloadSpeedUpdate;

        /// <summary>
        ///     Used to compute and display the download speed.
        /// </summary>
        private long lastTotalReceivedBytes;

        /// <summary>
        ///     Used when user clicks on 'Cancel' to abort all current downloads.
        /// </summary>
        private List<WebClient> pendingDownloads;

        /// <summary>
        ///     Used when user clicks on 'Cancel' to manage the cancelation (UI...).
        /// </summary>
        private bool userCancelled;

        #endregion Fields

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
                    Dispatcher.BeginInvoke(
                        new Action(() => { labelVersion.Content += " - A new version is available"; }));
            }
            else
            {
                failedToRetrieveLatestVersion = true;
            }

            if (failedToRetrieveLatestVersion)
                Dispatcher.BeginInvoke(new Action(() => { labelVersion.Content += " - Could not check for updates"; }));
        }

        /// <summary>
        ///     Downloads an album.
        /// </summary>
        /// <param name="album">The album to download.</param>
        /// <param name="downloadsFolder">The downloads folder.</param>
        /// <param name="tagTracks">True to tag tracks; false otherwise.</param>
        /// <param name="saveCoverArtInTags">True to save cover art in tags; false otherwise.</param>
        /// <param name="saveCovertArtInFolder">True to save cover art in the downloads folder; false otherwise.</param>
        /// <param name="convertCoverArtToJpg">True to convert the cover art to jpg; false otherwise.</param>
        /// <param name="resizeCoverArt">True to resize the covert art; false otherwise.</param>
        /// <param name="coverArtMaxSize">The maximum width/height of the cover art when resizing.</param>
        private void DownloadAlbum(Album album, string downloadsFolder, bool tagTracks, bool saveCoverArtInTags,
            bool saveCovertArtInFolder, bool convertCoverArtToJpg, bool resizeCoverArt, int coverArtMaxSize)
        {
            if (userCancelled) return;

            // Create directory to place track files
            try
            {
                Directory.CreateDirectory(downloadsFolder);
            }
            catch
            {
                Log(
                    "An error occured when creating the album folder. Make sure you have the rights to write files in the folder you chose",
                    LogType.Error);
                return;
            }

            Picture artwork = null;

            // Download artwork
            if (saveCoverArtInTags || saveCovertArtInFolder)
                artwork = DownloadCoverArt(album, downloadsFolder, saveCovertArtInFolder, convertCoverArtToJpg,
                    resizeCoverArt, coverArtMaxSize);

            // Download & tag tracks
            var tasks = new Task[album.Tracks.Count];
            var tracksDownloaded = new bool[album.Tracks.Count];
            for (var i = 0; i < album.Tracks.Count; i++)
            {
                // Temporarily save the index or we will have a race condition exception when i hits its maximum value
                var currentIndex = i;
                tasks[currentIndex] = Task.Factory.StartNew(() =>
                    tracksDownloaded[currentIndex] = DownloadAndTagTrack(downloadsFolder, album,
                        album.Tracks[currentIndex], tagTracks, saveCoverArtInTags, artwork));
            }

            // Wait for all tracks to be downloaded before saying the album is downloaded
            Task.WaitAll(tasks);

            if (!userCancelled)
            {
                // Tasks have not been aborted
                if (tracksDownloaded.All(x => x))
                    Log($"Successfully downloaded album \"{album.Title}\"", LogType.Success);
                else
                    Log($"Finished downloading album \"{album.Title}\". Some tracks were not downloaded",
                        LogType.Success);
            }
        }

        /// <summary>
        ///     Downloads and tags a track. Returns true if the track has been correctly downloaded; false otherwise.
        /// </summary>
        /// <param name="albumDirectoryPath">The path where to save the tracks.</param>
        /// <param name="album">The album of the track to download.</param>
        /// <param name="track">The track to download.</param>
        /// <param name="tagTrack">True to tag the track; false otherwise.</param>
        /// <param name="saveCoverArtInTags">True to save the cover art in the tag tracks; false otherwise.</param>
        /// <param name="artwork">The cover art.</param>
        private bool DownloadAndTagTrack(string albumDirectoryPath, Album album, Track track, bool tagTrack,
            bool saveCoverArtInTags, Picture artwork)
        {
            Log($"Downloading track \"{track.Title}\" from url: {track.Mp3Url}", LogType.VerboseInfo);

            // Set location to save the file
            var trackPath = albumDirectoryPath + "\\" + GetFileName(album, track);
            if (trackPath.Length > 256)
                trackPath = albumDirectoryPath + "\\" + GetFileName(album, track).Substring(0, 3) +
                            Path.GetExtension(trackPath);
            var tries = 0;
            var trackDownloaded = false;

            if (File.Exists(trackPath))
            {
                var length = new FileInfo(trackPath).Length;
                foreach (var trackFile in filesDownload)
                    if (track.Mp3Url == trackFile.Url &&
                        trackFile.Size > length - trackFile.Size * userSettings.AllowableFileSizeDifference &&
                        trackFile.Size < length + trackFile.Size * userSettings.AllowableFileSizeDifference)
                    {
                        Log(
                            $"Track already exists within allowed filesize range: track \"{GetFileName(album, track)}\" from album \"{album.Title}\" - Skipping download!",
                            LogType.IntermediateSuccess);
                        return false;
                    }
            }

            do
            {
                var doneEvent = new AutoResetEvent(false);

                using (var webClient = new WebClient())
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    // Update progress bar when downloading
                    webClient.DownloadProgressChanged += (s, e) => { UpdateProgress(track.Mp3Url, e.BytesReceived); };

                    // Warn & tag when downloaded
                    webClient.DownloadFileCompleted += (s, e) =>
                    {
                        WaitForCooldown(tries);
                        tries++;

                        if (!e.Cancelled && e.Error == null)
                        {
                            trackDownloaded = true;

                            if (tagTrack)
                            {
                                // Tag (ID3) the file when downloaded
                                var tagFile = TagLib.File.Create(trackPath);
                                tagFile.Tag.Album = album.Title;
                                tagFile.Tag.AlbumArtists = new string[1] {album.Artist};
                                tagFile.Tag.Performers = new string[1] {album.Artist};
                                tagFile.Tag.Title = track.Title;
                                tagFile.Tag.Track = (uint) track.Number;
                                tagFile.Tag.Year = (uint) album.ReleaseDate.Year;
                                tagFile.Tag.Lyrics = track.Lyrics;
                                tagFile.Save();
                            }

                            if (saveCoverArtInTags && artwork != null)
                            {
                                // Save cover in tags when downloaded
                                var tagFile = TagLib.File.Create(trackPath);
                                tagFile.Tag.Pictures = new IPicture[1] {artwork};
                                tagFile.Save();
                            }

                            // Note the file as downloaded
                            var currentFile = filesDownload.Where(f => f.Url == track.Mp3Url).First();
                            currentFile.Downloaded = true;
                            Log($"Downloaded track \"{GetFileName(album, track)}\" from album \"{album.Title}\"",
                                LogType.IntermediateSuccess);
                        }
                        else if (!e.Cancelled && e.Error != null)
                        {
                            if (tries < userSettings.DownloadMaxTries)
                                Log(
                                    $"Unable to download track \"{GetFileName(album, track)}\" from album \"{album.Title}\". Try {tries} of {userSettings.DownloadMaxTries}",
                                    LogType.Warning);
                            else
                                Log(
                                    $"Unable to download track \"{GetFileName(album, track)}\" from album \"{album.Title}\". Hit max retries of {userSettings.DownloadMaxTries}",
                                    LogType.Error);
                        } // Else the download has been cancelled (by the user)

                        doneEvent.Set();
                    };

                    lock (pendingDownloads)
                    {
                        if (userCancelled) return false;
                        // Register current download
                        pendingDownloads.Add(webClient);
                        // Start download
                        webClient.DownloadFileAsync(new Uri(track.Mp3Url), trackPath);
                    }

                    // Wait for download to be finished
                    doneEvent.WaitOne();
                    lock (pendingDownloads)
                    {
                        pendingDownloads.Remove(webClient);
                    }
                }
            } while (!trackDownloaded && tries < userSettings.DownloadMaxTries);

            return trackDownloaded;
        }

        /// <summary>
        ///     Downloads the cover art.
        /// </summary>
        /// <param name="album">The album to download.</param>
        /// <param name="downloadsFolder">The downloads folder.</param>
        /// <param name="saveCovertArtInFolder">True to save cover art in the downloads folder; false otherwise.</param>
        /// <param name="convertCoverArtToJpg">True to convert the cover art to jpg; false otherwise.</param>
        /// <param name="resizeCoverArt">True to resize the covert art; false otherwise.</param>
        /// <param name="coverArtMaxSize">The maximum width/height of the cover art when resizing.</param>
        /// <returns></returns>
        private Picture DownloadCoverArt(Album album, string downloadsFolder, bool saveCovertArtInFolder,
            bool convertCoverArtToJpg, bool resizeCoverArt, int coverArtMaxSize)
        {
            // Compute path where to save artwork
            var artworkPath = (saveCovertArtInFolder ? downloadsFolder : Path.GetTempPath()) + "\\" +
                              album.Title.ToAllowedFileName() + Path.GetExtension(album.ArtworkUrl);
            if (artworkPath.Length > 256)
                artworkPath = (saveCovertArtInFolder ? downloadsFolder : Path.GetTempPath()) + "\\" +
                              album.Title.ToAllowedFileName().Substring(0, 3) + Path.GetExtension(album.ArtworkUrl);

            Picture artwork = null;

            var tries = 0;
            var artworkDownloaded = false;

            do
            {
                var doneEvent = new AutoResetEvent(false);

                using (var webClient = new WebClient())
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    // Update progress bar when downloading
                    webClient.DownloadProgressChanged += (s, e) =>
                    {
                        UpdateProgress(album.ArtworkUrl, e.BytesReceived);
                    };

                    // Warn when downloaded
                    webClient.DownloadFileCompleted += (s, e) =>
                    {
                        if (!e.Cancelled && e.Error == null)
                        {
                            artworkDownloaded = true;

                            // Convert/resize artwork
                            if (userSettings.ConvertCoverArtToJpg || userSettings.ResizeCoverArt)
                            {
                                var settings = new ResizeSettings();
                                if (convertCoverArtToJpg)
                                {
                                    settings.Format = "jpg";
                                    settings.Quality = 90;
                                }

                                if (resizeCoverArt)
                                {
                                    settings.MaxHeight = userSettings.CoverArtMaxSize;
                                    settings.MaxWidth = userSettings.CoverArtMaxSize;
                                }

                                ImageBuilder.Current.Build(artworkPath, artworkPath, settings);
                            }

                            artwork = new Picture(artworkPath) {Description = "Picture"};

                            // Delete the cover art file if it was saved in Temp
                            if (!saveCovertArtInFolder)
                                try
                                {
                                    File.Delete(artworkPath);
                                }
                                catch
                                {
                                    // Could not delete the file. Nevermind, it's in Temp/ folder...
                                }

                            // Note the file as downloaded
                            var currentFile = filesDownload.Where(f => f.Url == album.ArtworkUrl).First();
                            currentFile.Downloaded = true;
                            Log($"Downloaded artwork for album \"{album.Title}\"", LogType.IntermediateSuccess);
                        }
                        else if (!e.Cancelled && e.Error != null)
                        {
                            if (tries < userSettings.DownloadMaxTries)
                                Log(
                                    $"Unable to download artwork for album \"{album.Title}\". Try {tries} of {userSettings.DownloadMaxTries}",
                                    LogType.Warning);
                            else
                                Log(
                                    $"Unable to download artwork for album \"{album.Title}\". Hit max retries of {userSettings.DownloadMaxTries}",
                                    LogType.Error);
                        } // Else the download has been cancelled (by the user)

                        doneEvent.Set();
                    };

                    lock (pendingDownloads)
                    {
                        if (userCancelled) return null;
                        // Register current download
                        pendingDownloads.Add(webClient);
                        // Start download
                        webClient.DownloadFileAsync(new Uri(album.ArtworkUrl), artworkPath);
                    }

                    // Wait for download to be finished
                    doneEvent.WaitOne();
                    lock (pendingDownloads)
                    {
                        pendingDownloads.Remove(webClient);
                    }
                }
            } while (!artworkDownloaded && tries < userSettings.DownloadMaxTries);

            return artwork;
        }

        /// <summary>
        ///     Returns the albums located at the specified URLs.
        /// </summary>
        /// <param name="urls">The URLs.</param>
        private List<Album> GetAlbums(List<string> urls)
        {
            var albums = new List<Album>();

            foreach (var url in urls)
            {
                Log($"Retrieving album data for {url}", LogType.Info);

                // Retrieve URL HTML source code
                var htmlCode = "";
                using (var webClient = new WebClient {Encoding = Encoding.UTF8})
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    if (userCancelled) return new List<Album>();

                    try
                    {
                        htmlCode = webClient.DownloadString(url);
                    }
                    catch
                    {
                        Log($"Could not retrieve data for {url}", LogType.Error);
                        continue;
                    }
                }

                // Get info on album
                try
                {
                    albums.Add(BandcampHelper.GetAlbum(htmlCode));
                }
                catch
                {
                    Log($"Could not retrieve album info for {url}", LogType.Error);
                }
            }

            return albums;
        }

        /// <summary>
        ///     Returns the artists discography from any URL (artist, album, track).
        /// </summary>
        /// <param name="urls">The URLs.</param>
        private List<string> GetArtistDiscography(List<string> urls)
        {
            var albumsUrls = new List<string>();

            foreach (var url in urls)
            {
                Log($"Retrieving artist discography from {url}", LogType.Info);

                // Retrieve URL HTML source code
                var htmlCode = "";
                using (var webClient = new WebClient {Encoding = Encoding.UTF8})
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    if (userCancelled) return new List<string>();

                    try
                    {
                        htmlCode = webClient.DownloadString(url);
                    }
                    catch
                    {
                        Log($"Could not retrieve data for {url}", LogType.Error);
                        continue;
                    }
                }

                // Get artist "music" bandcamp page (http://artist.bandcamp.com/music)
                var regex = new Regex("band_url = \"(?<url>.*)\"");
                if (!regex.IsMatch(htmlCode))
                {
                    Log(
                        $"No discography could be found on {url}. Try to uncheck the \"Download artist discography\" option",
                        LogType.Error);
                    continue;
                }

                var artistMusicPage = regex.Match(htmlCode).Groups["url"].Value + "/music";

                // Retrieve artist "music" page HTML source code
                using (var webClient = new WebClient {Encoding = Encoding.UTF8})
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    if (userCancelled) return new List<string>();

                    try
                    {
                        htmlCode = webClient.DownloadString(artistMusicPage);
                    }
                    catch
                    {
                        Log($"Could not retrieve data for {artistMusicPage}", LogType.Error);
                        continue;
                    }
                }

                // Get albums referred on the page
                regex = new Regex("TralbumData.*\n.*url:.*'/music'\n");
                if (!regex.IsMatch(htmlCode))
                    albumsUrls.Add(url);
                else
                    try
                    {
                        albumsUrls.AddRange(BandcampHelper.GetAlbumsUrl(htmlCode));
                    }
                    catch (NoAlbumFoundException)
                    {
                        Log(
                            $"No referred album could be found on {artistMusicPage}. Try to uncheck the \"Download artist discography\" option",
                            LogType.Error);
                    }
            }

            return albumsUrls;
        }

        /// <summary>
        ///     Replaces placeholders strings by the corresponding values in the specified filenameFormat location.
        /// </summary>
        /// <param name="downloadLocation">The download location to parse.</param>
        /// <param name="album">The album currently downloaded.</param>
        private string GetFileName(Album album, Track track)
        {
            var fileName =
                userSettings.FilenameFormat.Replace("{artist}", album.Artist)
                    .Replace("{title}", track.Title)
                    .Replace("{tracknum}", track.Number.ToString("00"));
            return fileName.ToAllowedFileName();
        }

        /// <summary>
        ///     Returns the files to download from a list of albums.
        /// </summary>
        /// <param name="albums">The albums.</param>
        /// <param name="downloadCoverArt">True if the cover arts must be downloaded, false otherwise.</param>
        private List<TrackFile> GetFilesToDownload(List<Album> albums, bool downloadCoverArt)
        {
            var files = new List<TrackFile>();
            foreach (var album in albums)
            {
                Log($"Computing size for album \"{album.Title}\"...", LogType.Info);

                // Artwork
                if (downloadCoverArt)
                {
                    long size = 0;
                    var sizeRetrieved = false;
                    var tries = 0;
                    if (userSettings.RetrieveFilesizes)
                        do
                        {
                            if (userCancelled) return new List<TrackFile>();
                            WaitForCooldown(tries);
                            tries++;
                            try
                            {
                                size = FileHelper.GetFileSize(album.ArtworkUrl, "HEAD");
                                sizeRetrieved = true;
                                Log($"Retrieved the size of the cover art file for album \"{album.Title}\"",
                                    LogType.VerboseInfo);
                            }
                            catch
                            {
                                sizeRetrieved = false;
                                if (tries < userSettings.DownloadMaxTries)
                                    Log(
                                        $"Failed to retrieve the size of the cover art file for album \"{album.Title}\". Try {tries} of {userSettings.DownloadMaxTries}",
                                        LogType.Warning);
                                else
                                    Log(
                                        $"Failed to retrieve the size of the cover art file for album \"{album.Title}\". Hit max retries of {userSettings.DownloadMaxTries}. Progress update may be wrong.",
                                        LogType.Error);
                            }
                        } while (!sizeRetrieved && tries < userSettings.DownloadMaxTries);

                    files.Add(new TrackFile(album.ArtworkUrl, 0, size));
                }

                // Tracks
                foreach (var track in album.Tracks)
                {
                    long size = 0;
                    var sizeRetrieved = false;
                    var tries = 0;
                    if (userSettings.RetrieveFilesizes)
                        do
                        {
                            if (userCancelled) return new List<TrackFile>();
                            WaitForCooldown(tries);
                            tries++;
                            try
                            {
                                // Using the HEAD method on tracks urls does not work (Error 405: Method not allowed)
                                // Surprisingly, using the GET method does not seem to download the whole file, so we will use it to retrieve
                                // the mp3 sizes
                                size = FileHelper.GetFileSize(track.Mp3Url, "GET");
                                sizeRetrieved = true;
                                Log($"Retrieved the size of the MP3 file for the track \"{track.Title}\"",
                                    LogType.VerboseInfo);
                            }
                            catch
                            {
                                sizeRetrieved = false;
                                if (tries < userSettings.DownloadMaxTries)
                                    Log(
                                        $"Failed to retrieve the size of the MP3 file for the track \"{track.Title}\". Try {tries} of {userSettings.DownloadMaxTries}",
                                        LogType.Warning);
                                else
                                    Log(
                                        $"Failed to retrieve the size of the MP3 file for the track \"{track.Title}\". Hit max retries of {userSettings.DownloadMaxTries}. Progress update may be wrong.",
                                        LogType.Error);
                            }
                        } while (!sizeRetrieved && tries < userSettings.DownloadMaxTries);

                    files.Add(new TrackFile(track.Mp3Url, 0, size));
                }
            }

            return files;
        }

        /// <summary>
        ///     Displays the specified message in the log with the specified color.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="color">The color.</param>
        private void Log(string message, LogType logType)
        {
            if (!userSettings.ShowVerboseLog && (logType == LogType.Warning || logType == LogType.VerboseInfo)) return;

            Dispatcher.Invoke(() =>
            {
                // Time
                var textRange = new TextRange(richTextBoxLog.Document.ContentEnd, richTextBoxLog.Document.ContentEnd);
                textRange.Text = DateTime.Now.ToString("HH:mm:ss") + " ";
                textRange.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Gray);
                // Message
                textRange = new TextRange(richTextBoxLog.Document.ContentEnd, richTextBoxLog.Document.ContentEnd);
                textRange.Text = message;
                textRange.ApplyPropertyValue(TextElement.ForegroundProperty, LogHelper.GetColor(logType));
                // Line break

                richTextBoxLog.AppendText(Environment.NewLine);
                if (userSettings.AutoScrollLog) richTextBoxLog.ScrollToEnd();
            });
        }

        /// <summary>
        ///     Replaces placeholders strings by the corresponding values in the specified download location.
        /// </summary>
        /// <param name="downloadLocation">The download location to parse.</param>
        /// <param name="album">The album currently downloaded.</param>
        private string ParseDownloadLocation(string downloadLocation, Album album)
        {
            downloadLocation =
                downloadLocation.Replace("{year}", album.ReleaseDate.Year.ToString().ToAllowedFileName());
            downloadLocation =
                downloadLocation.Replace("{month}", album.ReleaseDate.Month.ToString().ToAllowedFileName());
            downloadLocation = downloadLocation.Replace("{day}", album.ReleaseDate.Day.ToString().ToAllowedFileName());
            downloadLocation = downloadLocation.Replace("{artist}", album.Artist.ToAllowedFileName());
            downloadLocation = downloadLocation.Replace("{album}", album.Title.ToAllowedFileName());
            return downloadLocation;
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
        ///     Updates the progress messages and the progressbar.
        /// </summary>
        /// <param name="fileUrl">The URL of the file that just progressed.</param>
        /// <param name="bytesReceived">The received bytes for the specified file.</param>
        private void UpdateProgress(string fileUrl, long bytesReceived)
        {
            var now = DateTime.Now;

            lock (filesDownload)
            {
                // Compute new progress values
                var currentFile = filesDownload.Where(f => f.Url == fileUrl).First();
                currentFile.BytesReceived = bytesReceived;
                var totalReceivedBytes = filesDownload.Sum(f => f.BytesReceived);
                var bytesToDownload = filesDownload.Sum(f => f.Size);
                double downloadedFilesCount = filesDownload.Count(f => f.Downloaded);

                double bytesPerSecond;
                if (lastTotalReceivedBytes == 0)
                {
                    // First time we update the progress
                    bytesPerSecond = 0;
                    lastTotalReceivedBytes = totalReceivedBytes;
                    lastDownloadSpeedUpdate = now;
                }
                else if ((now - lastDownloadSpeedUpdate).TotalMilliseconds > 500)
                {
                    // Last update of progress happened more than 500 milliseconds ago
                    // We only update the download speed every 500+ milliseconds
                    bytesPerSecond =
                        (totalReceivedBytes - lastTotalReceivedBytes) /
                        (now - lastDownloadSpeedUpdate).TotalSeconds;
                    lastTotalReceivedBytes = totalReceivedBytes;
                    lastDownloadSpeedUpdate = now;

                    // Update UI
                    Dispatcher.Invoke(() =>
                    {
                        // Update download speed
                        labelDownloadSpeed.Content = (bytesPerSecond / 1024).ToString("0.0") + " kB/s";
                    });
                }

                // Update UI
                Dispatcher.Invoke(() =>
                {
                    if (!userCancelled)
                    {
                        // Update progress label
                        labelProgress.Content =
                            ((double) totalReceivedBytes / (1024 * 1024)).ToString("0.00") + " MB" +
                            (userSettings.RetrieveFilesizes
                                ? " / " + ((double) bytesToDownload / (1024 * 1024)).ToString("0.00") + " MB"
                                : "");
                        if (userSettings.RetrieveFilesizes)
                        {
                            // Update progress bar based on bytes received
                            progressBar.Value = totalReceivedBytes;
                            // Taskbar progress is between 0 and 1
                            TaskbarItemInfo.ProgressValue = totalReceivedBytes / progressBar.Maximum;
                        }
                        else
                        {
                            // Update progress bar based on downloaded files
                            progressBar.Value = downloadedFilesCount;
                            // Taskbar progress is between 0 and count of files to download
                            TaskbarItemInfo.ProgressValue = downloadedFilesCount / progressBar.Maximum;
                        }
                    }
                });
            }
        }

        private void WaitForCooldown(int NumTries)
        {
            if (userSettings.DownloadRetryCooldown != 0)
                Thread.Sleep((int) (Math.Pow(userSettings.DownloadRetryExponential, NumTries) *
                                    userSettings.DownloadRetryCooldown * 1000));
        }

        #endregion Methods

        #region Events

        private void buttonBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderBrowserDialog();
            dialog.Description = "Select the folder to save albums";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                textBoxDownloadsLocation.Text = dialog.SelectedPath;
        }

        private void buttonDefaultSettings_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Reset settings to their default values?", "Bandcamp Downloader",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) ==
                MessageBoxResult.OK) InitializeSettings(true);
        }

        private void buttonStart_Click(object sender, RoutedEventArgs e)
        {
            if (textBoxUrls.Text == Constants.UrlsHint)
            {
                // No URL to look
                Log("Paste some albums URLs to be downloaded", LogType.Error);
                return;
            }

            var coverArtMaxSize = 0;
            if (checkBoxResizeCoverArt.IsChecked.Value &&
                !int.TryParse(textBoxCoverArtMaxSize.Text, out coverArtMaxSize))
            {
                Log("Cover art max width/height must be an integer", LogType.Error);
                return;
            }

            userCancelled = false;

            pendingDownloads = new List<WebClient>();

            // Set controls to "downloading..." state
            activeDownloads = true;
            UpdateControlsState(true);

            Log("Starting download...", LogType.Info);

            // Get user inputs
            var userUrls = textBoxUrls.Text.Split(new[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            userUrls = userUrls.Distinct().ToList();

            var urls = new List<string>();
            var albums = new List<Album>();

            Task.Factory.StartNew(() =>
            {
                // Get URLs of albums to download
                if (userSettings.DownloadArtistDiscography)
                    urls = GetArtistDiscography(userUrls);
                else
                    urls = userUrls;
                urls = urls.Distinct().ToList();
            }).ContinueWith(x =>
            {
                // Get info on albums
                albums = GetAlbums(urls);
            }).ContinueWith(x =>
            {
                // Save files to download (we'll need the list to update the progressBar)
                filesDownload = GetFilesToDownload(albums,
                    userSettings.SaveCoverArtInTags || userSettings.SaveCoverArtInFolder);
            }).ContinueWith(x =>
            {
                // Set progressBar max value
                long maxProgressBarValue;
                if (userSettings.RetrieveFilesizes)
                    maxProgressBarValue = filesDownload.Sum(f => f.Size); // Bytes to download
                else
                    maxProgressBarValue = filesDownload.Count; // Number of files to download
                if (maxProgressBarValue > 0)
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.IsIndeterminate = false;
                        progressBar.Maximum = maxProgressBarValue;
                        TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
                    });
            }).ContinueWith(x =>
            {
                // Start downloading albums
                if (userSettings.DownloadOneAlbumAtATime)
                {
                    // Download one album at a time
                    foreach (var album in albums)
                        DownloadAlbum(album, ParseDownloadLocation(userSettings.DownloadsLocation, album),
                            userSettings.TagTracks, userSettings.SaveCoverArtInTags, userSettings.SaveCoverArtInFolder,
                            userSettings.ConvertCoverArtToJpg, userSettings.ResizeCoverArt,
                            userSettings.CoverArtMaxSize);
                }
                else
                {
                    // Parallel download
                    var tasks = new Task[albums.Count];
                    for (var i = 0; i < albums.Count; i++)
                    {
                        var album = albums[i]; // Mandatory or else => race condition
                        tasks[i] = Task.Factory.StartNew(() =>
                            DownloadAlbum(album, ParseDownloadLocation(userSettings.DownloadsLocation, album),
                                userSettings.TagTracks, userSettings.SaveCoverArtInTags,
                                userSettings.SaveCoverArtInFolder, userSettings.ConvertCoverArtToJpg,
                                userSettings.ResizeCoverArt, userSettings.CoverArtMaxSize));
                    }

                    // Wait for all albums to be downloaded
                    Task.WaitAll(tasks);
                }
            }).ContinueWith(x =>
            {
                if (userCancelled) Log("Downloads cancelled by user", LogType.Info);
                // Set controls to "ready" state
                activeDownloads = false;
                UpdateControlsState(false);
                // Play a sound
                try
                {
                    new SoundPlayer(@"C:\Windows\Media\Windows Ding.wav").Play();
                }
                catch
                {
                }
            });
        }

        private void buttonStop_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Would you like to cancel all downloads?", "Bandcamp Downloader",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) !=
                MessageBoxResult.Yes) return;

            userCancelled = true;
            Cursor = Cursors.Wait;
            Log("Cancelling downloads. Please wait...", LogType.Info);

            lock (pendingDownloads)
            {
                if (pendingDownloads.Count == 0)
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

            lock (pendingDownloads)
            {
                // Stop current downloads
                foreach (var webClient in pendingDownloads) webClient.CancelAsync();
            }

            Cursor = Cursors.Arrow;
        }

        private void InitializeSettings(bool resetToDefaults)
        {
            if (resetToDefaults) File.Delete(Constants.UserSettingsFilePath);
            // Must set this before UI forms, cannot default in settings as it isn't determined by a constant function
            if (string.IsNullOrEmpty(userSettings.DownloadsLocation))
                userSettings.DownloadsLocation =
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\{artist}\\{album}";
            userSettings = new ConfigurationBuilder<IUserSettings>().UseIniFile(Constants.UserSettingsFilePath).Build();
            DataContext = userSettings;
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
            if (activeDownloads)
                if (MessageBox.Show(
                        "There are currently active downloads. Are you sure you want to close the application and stop all downloads?",
                        "Bandcamp Downloader", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                        MessageBoxResult.Cancel) == MessageBoxResult.Cancel)
                    e.Cancel = true;
        }

        private void buttonTest_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = new MainViewModel();
            var window = new MainWindow(viewModel);
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Show();
        }

        #endregion Events
    }
}