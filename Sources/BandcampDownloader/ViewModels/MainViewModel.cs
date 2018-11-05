﻿using System;
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
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using BandcampDownloader.Annotations;
using BandcampDownloader.MediatorMessages;
using Config.Net;
using DevExpress.Mvvm;
using ImageResizer;
using TagLib;
using File = System.IO.File;

namespace BandcampDownloader.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {


        #region INotifyPropertyChanged Fields

        private string downloadLocationString;

        public string DownloadLocationString
        {
            get => downloadLocationString;
            set
            {
                if (value == downloadLocationString) return;
                downloadLocationString = value;
                OnPropertyChanged();
            }
        }

        private bool downloadStarted;

        public bool DownloadStarted
        {
            get => downloadStarted;
            set
            {
                if (value == downloadStarted) return;
                downloadStarted = value;
                OnPropertyChanged(nameof(DownloadStarted));
            }
        }

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

        private UserSettings userSettings;


        public UserSettings UserSettings
        {
            get => new ConfigurationBuilder<UserSettings>().UseIniFile(Constants.UserSettingsFilePath).Build();
            set
            {
                if (value == userSettings)
                {
                    return;
                }

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


        public ObservableCollection<TrackFile> FilesDownload;
        public ObservableCollection<WebClient> PendingDownloads;

        #endregion


        public MainViewModel()
        {
            ServicePointManager.DefaultConnectionLimit = 50;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            versionText = "v" + Assembly.GetEntryAssembly().GetName().Version;
            Task.Factory.StartNew(() => { CheckForUpdates(); });

        }

        #region Methods

        /// <summary>
        /// Displays a message if a new version is available.
        /// </summary>
        /// 
        public void CheckForUpdates()
        {
            // Note: GitHub uses a HTTP redirect to redirect from the generic latest release page to the actual latest release page
            Boolean failedToRetrieveLatestVersion = false;

            // Retrieve the redirect page from the GitHub latest release page
            HttpWebRequest request = HttpWebRequest.CreateHttp(Constants.LatestReleaseWebsite);
            request.AllowAutoRedirect = false;
            String redirectPage = "";
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
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
            String latestVersionNumber = "";
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
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion.CompareTo(latestVersion) < 0)
                {
                    // The latest version is newer than the current one
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                        versionText += " - A new version is available";
                    }));

                }
            }
            else
            {
                failedToRetrieveLatestVersion = true;
            }

            if (failedToRetrieveLatestVersion)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                    versionText += " - Could not check for updates";
                }));
            }
        }


        /// <summary>
        /// Downloads the cover art.
        /// </summary>
        /// <param name="album">The album to download.</param>
        /// <param name="downloadsFolder">The downloads folder.</param>
        /// <param name="saveCovertArtInFolder">True to save cover art in the downloads folder; false otherwise.</param>
        /// <param name="convertCoverArtToJpg">True to convert the cover art to jpg; false otherwise.</param>
        /// <param name="resizeCoverArt">True to resize the covert art; false otherwise.</param>
        /// <param name="coverArtMaxSize">The maximum width/height of the cover art when resizing.</param>
        /// <returns></returns>
        public Picture DownloadCoverArt(Album album, String downloadsFolder, Boolean saveCovertArtInFolder, Boolean convertCoverArtToJpg, Boolean resizeCoverArt, int coverArtMaxSize)
        {
            // Compute path where to save artwork
            String artworkPath = (saveCovertArtInFolder ? downloadsFolder : Path.GetTempPath()) + "\\" + album.Title.ToAllowedFileName() + Path.GetExtension(album.ArtworkUrl);
            if (artworkPath.Length > 256)
            {
                // Shorten the path (Windows doesn't support a path > 256 characters)
                artworkPath = (saveCovertArtInFolder ? downloadsFolder : Path.GetTempPath()) + "\\" + album.Title.ToAllowedFileName().Substring(0, 3) + Path.GetExtension(album.ArtworkUrl);
            }

            Picture artwork = null;

            int tries = 0;
            Boolean artworkDownloaded = false;

            do
            {
                var doneEvent = new AutoResetEvent(false);

                using (var webClient = new WebClient())
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    // Update progress bar when downloading
                    webClient.DownloadProgressChanged += (s, e) => {
                        UpdateProgress(album.ArtworkUrl, e.BytesReceived);
                    };

                    // Warn when downloaded
                    webClient.DownloadFileCompleted += (s, e) => {
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

                            artwork = new Picture(artworkPath) { Description = "Picture" };

                            // Delete the cover art file if it was saved in Temp
                            if (!saveCovertArtInFolder)
                            {
                                try
                                {
                                    File.Delete(artworkPath);
                                }
                                catch
                                {
                                    // Could not delete the file. Nevermind, it's in Temp/ folder...
                                }
                            }

                            // Note the file as downloaded
                            TrackFile currentFile = FilesDownload.Where(f => f.Url == album.ArtworkUrl).First();
                            currentFile.Downloaded = true;
                            Log($"Downloaded artwork for album \"{album.Title}\"", LogType.IntermediateSuccess);
                        }
                        else if (!e.Cancelled && e.Error != null)
                        {
                            if (tries < userSettings.DownloadMaxTries)
                            {
                                Log($"Unable to download artwork for album \"{album.Title}\". Try {tries} of {userSettings.DownloadMaxTries}", LogType.Warning);
                            }
                            else
                            {
                                Log($"Unable to download artwork for album \"{album.Title}\". Hit max retries of {userSettings.DownloadMaxTries}", LogType.Error);
                            }
                        } // Else the download has been cancelled (by the user)

                        doneEvent.Set();
                    };

                    lock (PendingDownloads)
                    {
                        if (UserCanceled)
                        {
                            // Abort
                            return null;
                        }
                        // Register current download
                        PendingDownloads.Add(webClient);
                        // Start download
                        webClient.DownloadFileAsync(new Uri(album.ArtworkUrl), artworkPath);
                    }

                    // Wait for download to be finished
                    doneEvent.WaitOne();
                    lock (PendingDownloads)
                    {
                        PendingDownloads.Remove(webClient);
                    }
                }
            } while (!artworkDownloaded && tries < userSettings.DownloadMaxTries);

            return artwork;
        }


        /// <summary>
        /// Downloads an album.
        /// </summary>
        /// <param name="album">The album to download.</param>
        /// <param name="downloadsFolder">The downloads folder.</param>
        /// <param name="tagTracks">True to tag tracks; false otherwise.</param>
        /// <param name="saveCoverArtInTags">True to save cover art in tags; false otherwise.</param>
        /// <param name="saveCovertArtInFolder">True to save cover art in the downloads folder; false otherwise.</param>
        /// <param name="convertCoverArtToJpg">True to convert the cover art to jpg; false otherwise.</param>
        /// <param name="resizeCoverArt">True to resize the covert art; false otherwise.</param>
        /// <param name="coverArtMaxSize">The maximum width/height of the cover art when resizing.</param>
        public void DownloadAlbum(Album album, String downloadsFolder, Boolean tagTracks, Boolean saveCoverArtInTags, Boolean saveCovertArtInFolder, Boolean convertCoverArtToJpg, Boolean resizeCoverArt, int coverArtMaxSize)
        {
            if (userCanceled)
            {
                // Abort
                return;
            }

            // Create directory to place track files
            try
            {
                Directory.CreateDirectory(downloadsFolder);
            }
            catch
            {
                Log("An error occured when creating the album folder. Make sure you have the rights to write files in the folder you chose", LogType.Error);
                return;
            }

            Picture artwork = null;

            // Download artwork
            if (saveCoverArtInTags || saveCovertArtInFolder)
            {
                artwork = DownloadCoverArt(album, downloadsFolder, saveCovertArtInFolder, convertCoverArtToJpg, resizeCoverArt, coverArtMaxSize);
            }

            // Download & tag tracks
            Task[] tasks = new Task[album.Tracks.Count];
            Boolean[] tracksDownloaded = new Boolean[album.Tracks.Count];
            for (int i = 0; i < album.Tracks.Count; i++)
            {
                // Temporarily save the index or we will have a race condition exception when i hits its maximum value
                int currentIndex = i;
                tasks[currentIndex] = Task.Factory.StartNew(() => tracksDownloaded[currentIndex] = DownloadAndTagTrack(downloadsFolder, album, album.Tracks[currentIndex], tagTracks, saveCoverArtInTags, artwork));
            }

            // Wait for all tracks to be downloaded before saying the album is downloaded
            Task.WaitAll(tasks);

            if (!userCanceled)
            {
                // Tasks have not been aborted
                if (tracksDownloaded.All(x => x == true))
                {
                    Log($"Successfully downloaded album \"{album.Title}\"", LogType.Success);
                }
                else
                {
                    Log($"Finished downloading album \"{album.Title}\". Some tracks were not downloaded", LogType.Success);
                }
            }
        }

        /// <summary>
        /// Downloads and tags a track. Returns true if the track has been correctly downloaded; false otherwise.
        /// </summary>
        /// <param name="albumDirectoryPath">The path where to save the tracks.</param>
        /// <param name="album">The album of the track to download.</param>
        /// <param name="track">The track to download.</param>
        /// <param name="tagTrack">True to tag the track; false otherwise.</param>
        /// <param name="saveCoverArtInTags">True to save the cover art in the tag tracks; false otherwise.</param>
        /// <param name="artwork">The cover art.</param>
        public Boolean DownloadAndTagTrack(String albumDirectoryPath, Album album, Track track, Boolean tagTrack, Boolean saveCoverArtInTags, Picture artwork)
        {
            Log($"Downloading track \"{track.Title}\" from url: {track.Mp3Url}", LogType.VerboseInfo);

            // Set location to save the file
            String trackPath = albumDirectoryPath + "\\" + GetFileName(album, track);
            if (trackPath.Length > 256)
            {
                // Shorten the path (Windows doesn't support a path > 256 characters)
                trackPath = albumDirectoryPath + "\\" + GetFileName(album, track).Substring(0, 3) + Path.GetExtension(trackPath);
            }
            int tries = 0;
            Boolean trackDownloaded = false;

            if (File.Exists(trackPath))
            {
                long length = new FileInfo(trackPath).Length;
                foreach (TrackFile trackFile in FilesDownload)
                {
                    if (track.Mp3Url == trackFile.Url &&
                        trackFile.Size > length - (trackFile.Size * userSettings.AllowableFileSizeDifference) &&
                        trackFile.Size < length + (trackFile.Size * userSettings.AllowableFileSizeDifference))
                    {
                        Log($"Track already exists within allowed filesize range: track \"{GetFileName(album, track)}\" from album \"{album.Title}\" - Skipping download!", LogType.IntermediateSuccess);
                        return false;
                    }
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
                    webClient.DownloadProgressChanged += (s, e) => {
                        UpdateProgress(track.Mp3Url, e.BytesReceived);
                    };

                    // Warn & tag when downloaded
                    webClient.DownloadFileCompleted += (s, e) => {
                        WaitForCooldown(tries);
                        tries++;

                        if (!e.Cancelled && e.Error == null)
                        {
                            trackDownloaded = true;

                            if (tagTrack)
                            {
                                // Tag (ID3) the file when downloaded
                                TagLib.File tagFile = TagLib.File.Create(trackPath);
                                tagFile.Tag.Album = album.Title;
                                tagFile.Tag.AlbumArtists = new String[1] { album.Artist };
                                tagFile.Tag.Performers = new String[1] { album.Artist };
                                tagFile.Tag.Title = track.Title;
                                tagFile.Tag.Track = (uint)track.Number;
                                tagFile.Tag.Year = (uint)album.ReleaseDate.Year;
                                tagFile.Tag.Lyrics = track.Lyrics;
                                tagFile.Save();
                            }

                            if (saveCoverArtInTags && artwork != null)
                            {
                                // Save cover in tags when downloaded
                                TagLib.File tagFile = TagLib.File.Create(trackPath);
                                tagFile.Tag.Pictures = new IPicture[1] { artwork };
                                tagFile.Save();
                            }

                            // Note the file as downloaded
                            TrackFile currentFile = FilesDownload.Where(f => f.Url == track.Mp3Url).First();
                            currentFile.Downloaded = true;
                            Log($"Downloaded track \"{GetFileName(album, track)}\" from album \"{album.Title}\"", LogType.IntermediateSuccess);
                        }
                        else if (!e.Cancelled && e.Error != null)
                        {
                            if (tries < userSettings.DownloadMaxTries)
                            {
                                Log($"Unable to download track \"{GetFileName(album, track)}\" from album \"{album.Title}\". Try {tries} of {userSettings.DownloadMaxTries}", LogType.Warning);
                            }
                            else
                            {
                                Log($"Unable to download track \"{GetFileName(album, track)}\" from album \"{album.Title}\". Hit max retries of {userSettings.DownloadMaxTries}", LogType.Error);
                            }
                        } // Else the download has been cancelled (by the user)

                        doneEvent.Set();
                    };

                    lock (PendingDownloads)
                    {
                        if (userCanceled)
                        {
                            // Abort
                            return false;
                        }
                        // Register current download
                        PendingDownloads.Add(webClient);
                        // Start download
                        webClient.DownloadFileAsync(new Uri(track.Mp3Url), trackPath);
                    }
                    // Wait for download to be finished
                    doneEvent.WaitOne();
                    lock (PendingDownloads)
                    {
                        PendingDownloads.Remove(webClient);
                    }
                }
            } while (!trackDownloaded && tries < userSettings.DownloadMaxTries);

            return trackDownloaded;
        }


        /// <summary>
        /// Returns the albums located at the specified URLs.
        /// </summary>
        /// <param name="urls">The URLs.</param>
        public List<Album> GetAlbums(List<String> urls)
        {
            var albums = new List<Album>();

            foreach (String url in urls)
            {
                Log($"Retrieving album data for {url}", LogType.Info);

                // Retrieve URL HTML source code
                String htmlCode = "";
                using (var webClient = new WebClient() { Encoding = Encoding.UTF8 })
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    if (userCanceled)
                    {
                        // Abort
                        return new List<Album>();
                    }

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
                    continue;
                }
            }

            return albums;
        }

        /// <summary>
        /// Replaces placeholders strings by the corresponding values in the specified download location.
        /// </summary>
        /// <param name="downloadLocation">The download location to parse.</param>
        /// <param name="album">The album currently downloaded.</param>
        public String ParseDownloadLocation(String downloadLocation, Album album)
        {
            downloadLocation = downloadLocation.Replace("{year}", album.ReleaseDate.Year.ToString().ToAllowedFileName());
            downloadLocation = downloadLocation.Replace("{month}", album.ReleaseDate.Month.ToString().ToAllowedFileName());
            downloadLocation = downloadLocation.Replace("{day}", album.ReleaseDate.Day.ToString().ToAllowedFileName());
            downloadLocation = downloadLocation.Replace("{artist}", album.Artist.ToAllowedFileName());
            downloadLocation = downloadLocation.Replace("{album}", album.Title.ToAllowedFileName());
            return downloadLocation;
        }

        public List<String> GetArtistDiscography(List<String> urls)
        {
            var albumsUrls = new List<String>();

            foreach (String url in urls)
            {
                Log($"Retrieving artist discography from {url}", LogType.Info);

                // Retrieve URL HTML source code
                String htmlCode = "";
                using (var webClient = new WebClient() { Encoding = Encoding.UTF8 })
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    if (userCanceled)
                    {
                        // Abort
                        return new List<String>();
                    }

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
                    Log($"No discography could be found on {url}. Try to uncheck the \"Download artist discography\" option", LogType.Error);
                    continue;
                }
                String artistMusicPage = regex.Match(htmlCode).Groups["url"].Value + "/music";

                // Retrieve artist "music" page HTML source code
                using (var webClient = new WebClient() { Encoding = Encoding.UTF8 })
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    if (userCanceled)
                    {
                        // Abort
                        return new List<String>();
                    }

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
                {
                    // This seem to be a one-album artist with no "music" page => URL redirects to the unique album URL
                    albumsUrls.Add(url);
                }
                else
                {
                    // We are on a real "music" page
                    try
                    {
                        albumsUrls.AddRange(BandcampHelper.GetAlbumsUrl(htmlCode));
                    }
                    catch (NoAlbumFoundException)
                    {
                        Log($"No referred album could be found on {artistMusicPage}. Try to uncheck the \"Download artist discography\" option", LogType.Error);
                        continue;
                    }
                }
            }

            return albumsUrls;
        }

        /// <summary>
        /// Replaces placeholders strings by the corresponding values in the specified filenameFormat location.
        /// </summary>
        /// <param name="downloadLocation">The download location to parse.</param>
        /// <param name="album">The album currently downloaded.</param>
        public String GetFileName(Album album, Track track)
        {
            String fileName =
                userSettings.FilenameFormat.Replace("{artist}", album.Artist)
                    .Replace("{title}", track.Title)
                    .Replace("{tracknum}", track.Number.ToString("00"));
            return fileName.ToAllowedFileName();
        }

        /// <summary>
        /// Returns the files to download from a list of albums.
        /// </summary>
        /// <param name="albums">The albums.</param>
        /// <param name="downloadCoverArt">True if the cover arts must be downloaded, false otherwise.</param>
        public ObservableCollection<TrackFile> GetFilesToDownload(List<Album> albums, Boolean downloadCoverArt)
        {
            var files = new ObservableCollection<TrackFile>();
            foreach (Album album in albums)
            {
                Log($"Computing size for album \"{album.Title}\"...", LogType.Info);

                // Artwork
                if (downloadCoverArt)
                {
                    long size = 0;
                    Boolean sizeRetrieved = false;
                    int tries = 0;
                    if (userSettings.RetrieveFilesizes)
                    {
                        do
                        {
                            if (userCanceled)
                            {
                                // Abort
                                return new ObservableCollection<TrackFile>();
                            }
                            WaitForCooldown(tries);
                            tries++;
                            try
                            {
                                size = FileHelper.GetFileSize(album.ArtworkUrl, "HEAD");
                                sizeRetrieved = true;
                                Log($"Retrieved the size of the cover art file for album \"{album.Title}\"", LogType.VerboseInfo);
                            }
                            catch
                            {
                                sizeRetrieved = false;
                                if (tries < userSettings.DownloadMaxTries)
                                {
                                    Log($"Failed to retrieve the size of the cover art file for album \"{album.Title}\". Try {tries} of {userSettings.DownloadMaxTries}", LogType.Warning);
                                }
                                else
                                {
                                    Log($"Failed to retrieve the size of the cover art file for album \"{album.Title}\". Hit max retries of {userSettings.DownloadMaxTries}. Progress update may be wrong.", LogType.Error);
                                }
                            }
                        } while (!sizeRetrieved && tries < userSettings.DownloadMaxTries);
                    }
                    files.Add(new TrackFile(album.ArtworkUrl, 0, size));
                }

                // Tracks
                foreach (Track track in album.Tracks)
                {
                    long size = 0;
                    Boolean sizeRetrieved = false;
                    int tries = 0;
                    if (userSettings.RetrieveFilesizes)
                        do
                        {
                            if (userCanceled)
                            {
                                // Abort
                                return new ObservableCollection<TrackFile>();
                            }
                            WaitForCooldown(tries);
                            tries++;
                            try
                            {
                                // Using the HEAD method on tracks urls does not work (Error 405: Method not allowed)
                                // Surprisingly, using the GET method does not seem to download the whole file, so we will use it to retrieve
                                // the mp3 sizes
                                size = FileHelper.GetFileSize(track.Mp3Url, "GET");
                                sizeRetrieved = true;
                                Log($"Retrieved the size of the MP3 file for the track \"{track.Title}\"", LogType.VerboseInfo);
                            }
                            catch
                            {
                                sizeRetrieved = false;
                                if (tries < userSettings.DownloadMaxTries)
                                {
                                    Log($"Failed to retrieve the size of the MP3 file for the track \"{track.Title}\". Try {tries} of {userSettings.DownloadMaxTries}", LogType.Warning);
                                }
                                else
                                {
                                    Log($"Failed to retrieve the size of the MP3 file for the track \"{track.Title}\". Hit max retries of {userSettings.DownloadMaxTries}. Progress update may be wrong.", LogType.Error);
                                }
                            }
                        } while (!sizeRetrieved && tries < userSettings.DownloadMaxTries);
                    files.Add(new TrackFile(track.Mp3Url, 0, size));
                }
            }
            return files;
        }

        public void WaitForCooldown(int NumTries)
        {
            if (userSettings.DownloadRetryCooldown != 0)
            {
                Thread.Sleep((int)((Math.Pow(userSettings.DownloadRetryExponential, NumTries)) * userSettings.DownloadRetryCooldown * 1000));
            }
        }


        /// <summary>
        /// Updates the progress messages and the progressbar.
        /// </summary>
        /// <param name="fileUrl">The URL of the file that just progressed.</param>
        /// <param name="bytesReceived">The received bytes for the specified file.</param>
        public void UpdateProgress(String fileUrl, long bytesReceived)
        {
            DateTime now = DateTime.Now;

            lock (FilesDownload)
            {
                // Compute new progress values
                TrackFile currentFile = FilesDownload.Where(f => f.Url == fileUrl).First();
                currentFile.BytesReceived = bytesReceived;
                long totalReceivedBytes = FilesDownload.Sum(f => f.BytesReceived);
                long bytesToDownload = FilesDownload.Sum(f => f.Size);
                Double downloadedFilesCount = FilesDownload.Count(f => f.Downloaded);

                Double bytesPerSecond;
                if (this.lastTotalReceivedBytes == 0)
                {
                    // First time we update the progress
                    bytesPerSecond = 0;
                    this.lastTotalReceivedBytes = totalReceivedBytes;
                    this.lastDownloadSpeedUpdate = now;
                }
                else if ((now - this.lastDownloadSpeedUpdate).TotalMilliseconds > 500)
                {
                    // Last update of progress happened more than 500 milliseconds ago
                    // We only update the download speed every 500+ milliseconds
                    bytesPerSecond =
                        ((Double)(totalReceivedBytes - this.lastTotalReceivedBytes)) /
                        (now - this.lastDownloadSpeedUpdate).TotalSeconds;
                    this.lastTotalReceivedBytes = totalReceivedBytes;
                    this.lastDownloadSpeedUpdate = now;

                    // Update UI
                    Application.Current.Dispatcher.Invoke(new Action(() => {
                        // Update download speed
                        downloadSpeedString = (bytesPerSecond / 1024).ToString("0.0") + " kB/s";
                    }));
                }

                // Update UI
                Application.Current.Dispatcher.Invoke(new Action(() => {
                    if (!userCanceled)
                    {
                        // Update progress label
                        progressString =
                            ((Double)totalReceivedBytes / (1024 * 1024)).ToString("0.00") + " MB" +
                            (userSettings.RetrieveFilesizes ? (" / " + ((Double)bytesToDownload / (1024 * 1024)).ToString("0.00") + " MB") : "");
                        if (userSettings.RetrieveFilesizes)
                        {
                            // Update progress bar based on bytes received
                            progressValue = totalReceivedBytes;
                            // Taskbar progress is between 0 and 1
                            progressBarValue= totalReceivedBytes / maximumProgressValue;
                        }
                        else
                        {
                            // Update progress bar based on downloaded files
                            progressValue = (long) downloadedFilesCount;
                            // Taskbar progress is between 0 and count of files to download
                            progressBarValue = (long) (downloadedFilesCount / maximumProgressValue);
                        }
                    }
                }));
            }
        }

        private void Log(string eventMessage, LogType logType)
        {
            Messenger.Default.Send(new DownloaderLogMessage(eventMessage, logType));
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}