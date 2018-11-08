using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Shell;
using BandcampDownloader.MediatorMessages;
using BandcampDownloader.ProgressReporters;
using DevExpress.Mvvm;
using ImageResizer;
using TagLib;
using File = TagLib.File;

namespace BandcampDownloader.Services
{
    public class DownloadService
    {

        public bool UserCanceled;
        public bool ActiveDownloads;
        public ObservableCollection<WebClient> PendingDownloads;
        public List<TrackFile> FilesDownload;
        public string Urls;
        public string DownloadSpeedString;
        private long maximumProgressValue;
        private long lastTotalReceivedBytes;
        private DateTime lastDownloadSpeedUpdate;
        private IProgress<DownloadProgressReporter> progressReporter;



        #region Methods

        public void StartDownload(IProgress<DownloadProgressReporter> reporter)
        {
            progressReporter = reporter;

            var coverArtMaxSize = 0;
            if (Globals.UserSettings.ConvertCoverArtToJpg &&
                !int.TryParse(Globals.UserSettings.CoverArtMaxSize.ToString(), out coverArtMaxSize))
            {
                Log("Cover art max width/height must be an integer", LogType.Error);
                return;
            }

            ActiveDownloads = true;
            UserCanceled = false;

            PendingDownloads = new ObservableCollection<WebClient>();

            // Set controls to "downloading..." state
            UpdateControlsState(true);

            Log("Starting download...", LogType.Info);

            // Get user inputs
            var userUrls = Urls.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
            userUrls = userUrls.Distinct().ToList();

            var urls = new List<string>();
            var albums = new List<Album>();

            Task.Factory.StartNew(() =>
            {
                // Get URLs of albums to download
                if (Globals.UserSettings.DownloadArtistDiscography)
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
                FilesDownload = GetFilesToDownload(albums,
                    Globals.UserSettings.SaveCoverArtInTags || Globals.UserSettings.SaveCoverArtInFolder);
            }).ContinueWith(x =>
            {
                // Set progressBar max value
                if (Globals.UserSettings.RetrieveFilesizes)
                    maximumProgressValue = FilesDownload.Sum(f => f.Size); // Bytes to download
                else
                    maximumProgressValue = FilesDownload.Count; // Number of files to download
                if (maximumProgressValue > 0)
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateProgressNormal(false, maximumProgressValue, TaskbarItemProgressState.Normal);
                    });
            }).ContinueWith(x =>
            {
                // Start downloading albums
                if (Globals.UserSettings.DownloadOneAlbumAtATime)
                {
                    // Download one album at a time

                    foreach (var album in albums)
                        DownloadAlbum(album, ParseDownloadLocation(
                                Globals.UserSettings.DownloadsLocation, album),
                            Globals.UserSettings.TagTracks,
                            Globals.UserSettings.SaveCoverArtInTags,
                            Globals.UserSettings.SaveCoverArtInFolder,
                            Globals.UserSettings.ConvertCoverArtToJpg,
                            Globals.UserSettings.ResizeCoverArt,
                            Globals.UserSettings.CoverArtMaxSize);
                }
                else
                {
                    // Parallel download
                    var tasks = new Task[albums.Count];
                    for (var i = 0; i < albums.Count; i++)
                    {
                        var album = albums[i]; // Mandatory or else => race condition
                        tasks[i] = Task.Factory.StartNew(() =>
                            DownloadAlbum(album,
                                ParseDownloadLocation(
                                    Globals.UserSettings.DownloadsLocation, album),
                                Globals.UserSettings.TagTracks,
                                Globals.UserSettings.SaveCoverArtInTags,
                                Globals.UserSettings.SaveCoverArtInFolder,
                                Globals.UserSettings.ConvertCoverArtToJpg,
                                Globals.UserSettings.ResizeCoverArt,
                                Globals.UserSettings.CoverArtMaxSize));
                    }

                    // Wait for all albums to be downloaded
                    Task.WaitAll(tasks);
                }
            }).ContinueWith(x =>
            {
                if (UserCanceled) Log("Downloads cancelled by user", LogType.Info);
                // Set controls to "ready" state
                ActiveDownloads = false;
                UpdateControlsState(false);
                // Play a sound
                try
                {
                    PlayASound();
                }
                catch
                {
                }
            });
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
        public Picture DownloadCoverArt(Album album, string downloadsFolder, bool saveCovertArtInFolder,
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
                            if (Globals.UserSettings.ConvertCoverArtToJpg || Globals.UserSettings.ResizeCoverArt)
                            {
                                var settings = new ResizeSettings();
                                if (convertCoverArtToJpg)
                                {
                                    settings.Format = "jpg";
                                    settings.Quality = 90;
                                }

                                if (resizeCoverArt)
                                {
                                    settings.MaxHeight = Globals.UserSettings.CoverArtMaxSize;
                                    settings.MaxWidth = Globals.UserSettings.CoverArtMaxSize;
                                }

                                ImageBuilder.Current.Build(artworkPath, artworkPath, settings);
                            }

                            artwork = new Picture(artworkPath) { Description = "Picture" };

                            // Delete the cover art file if it was saved in Temp
                            if (!saveCovertArtInFolder)
                                try
                                {
                                    System.IO.File.Delete(artworkPath);
                                }
                                catch
                                {
                                    // Could not delete the file. Nevermind, it's in Temp/ folder...
                                }

                            // Note the file as downloaded
                            var currentFile = FilesDownload.Where(f => f.Url == album.ArtworkUrl).First();
                            currentFile.Downloaded = true;
                            Log($"Downloaded artwork for album \"{album.Title}\"", LogType.IntermediateSuccess);
                        }
                        else if (!e.Cancelled && e.Error != null)
                        {
                            if (tries < Globals.UserSettings.DownloadMaxTries)
                                Log(
                                    $"Unable to download artwork for album \"{album.Title}\". Try {tries} of {Globals.UserSettings.DownloadMaxTries}",
                                    LogType.Warning);
                            else
                                Log(
                                    $"Unable to download artwork for album \"{album.Title}\". Hit max retries of {Globals.UserSettings.DownloadMaxTries}",
                                    LogType.Error);
                        } // Else the download has been cancelled (by the user)

                        doneEvent.Set();
                    };

                    lock (PendingDownloads)
                    {
                        if (UserCanceled) return null;
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
            } while (!artworkDownloaded && tries < Globals.UserSettings.DownloadMaxTries);

            return artwork;
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
        public void DownloadAlbum(Album album, string downloadsFolder, bool tagTracks, bool saveCoverArtInTags,
            bool saveCovertArtInFolder, bool convertCoverArtToJpg, bool resizeCoverArt, int coverArtMaxSize)
        {
            if (UserCanceled) return;

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

            if (!UserCanceled)
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
        public bool DownloadAndTagTrack(string albumDirectoryPath, Album album, Track track, bool tagTrack,
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

            if (System.IO.File.Exists(trackPath))
            {
                var length = new FileInfo(trackPath).Length;
                foreach (var trackFile in FilesDownload)
                    if (track.Mp3Url == trackFile.Url &&
                        trackFile.Size > length - trackFile.Size * Globals.UserSettings.AllowableFileSizeDifference &&
                        trackFile.Size < length + trackFile.Size * Globals.UserSettings.AllowableFileSizeDifference)
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
                                tagFile.Tag.AlbumArtists = new string[1] { album.Artist };
                                tagFile.Tag.Performers = new string[1] { album.Artist };
                                tagFile.Tag.Title = track.Title;
                                tagFile.Tag.Track = (uint)track.Number;
                                tagFile.Tag.Year = (uint)album.ReleaseDate.Year;
                                tagFile.Tag.Lyrics = track.Lyrics;
                                tagFile.Save();
                            }

                            if (saveCoverArtInTags && artwork != null)
                            {
                                // Save cover in tags when downloaded
                                var tagFile = TagLib.File.Create(trackPath);
                                tagFile.Tag.Pictures = new IPicture[1] { artwork };
                                tagFile.Save();
                            }

                            // Note the file as downloaded
                            var currentFile = FilesDownload.Where(f => f.Url == track.Mp3Url).First();
                            currentFile.Downloaded = true;
                            Log($"Downloaded track \"{GetFileName(album, track)}\" from album \"{album.Title}\"",
                                LogType.IntermediateSuccess);
                        }
                        else if (!e.Cancelled && e.Error != null)
                        {
                            if (tries < Globals.UserSettings.DownloadMaxTries)
                                Log(
                                    $"Unable to download track \"{GetFileName(album, track)}\" from album \"{album.Title}\". Try {tries} of {Globals.UserSettings.DownloadMaxTries}",
                                    LogType.Warning);
                            else
                                Log(
                                    $"Unable to download track \"{GetFileName(album, track)}\" from album \"{album.Title}\". Hit max retries of {Globals.UserSettings.DownloadMaxTries}",
                                    LogType.Error);
                        } // Else the download has been cancelled (by the user)

                        doneEvent.Set();
                    };

                    lock (PendingDownloads)
                    {
                        if (UserCanceled) return false;
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
            } while (!trackDownloaded && tries < Globals.UserSettings.DownloadMaxTries);

            return trackDownloaded;
        }


        /// <summary>
        ///     Returns the albums located at the specified URLs.
        /// </summary>
        /// <param name="urls">The URLs.</param>
        public List<Album> GetAlbums(List<string> urls)
        {
            var albums = new List<Album>();

            foreach (var url in urls)
            {
                Log($"Retrieving album data for {url}", LogType.Info);

                // Retrieve URL HTML source code
                var htmlCode = "";
                using (var webClient = new WebClient { Encoding = Encoding.UTF8 })
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    if (UserCanceled) return new List<Album>();

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
        ///     Replaces placeholders strings by the corresponding values in the specified download location.
        /// </summary>
        /// <param name="downloadLocation">The download location to parse.</param>
        /// <param name="album">The album currently downloaded.</param>
        public string ParseDownloadLocation(string downloadLocation, Album album)
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

        public List<string> GetArtistDiscography(List<string> urls)
        {
            var albumsUrls = new List<string>();

            foreach (var url in urls)
            {
                Log($"Retrieving artist discography from {url}", LogType.Info);

                // Retrieve URL HTML source code
                var htmlCode = "";
                using (var webClient = new WebClient { Encoding = Encoding.UTF8 })
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    if (UserCanceled) return new List<string>();

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
                using (var webClient = new WebClient { Encoding = Encoding.UTF8 })
                {
                    if (webClient.Proxy != null)
                        webClient.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;

                    if (UserCanceled) return new List<string>();

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
        public string GetFileName(Album album, Track track)
        {
            var fileName =
                Globals.UserSettings.FilenameFormat.Replace("{artist}", album.Artist)
                    .Replace("{title}", track.Title)
                    .Replace("{tracknum}", track.Number.ToString("00"));
            return fileName.ToAllowedFileName();
        }

        /// <summary>
        ///     Returns the files to download from a list of albums.
        /// </summary>
        /// <param name="albums">The albums.</param>
        /// <param name="downloadCoverArt">True if the cover arts must be downloaded, false otherwise.</param>
        public List<TrackFile> GetFilesToDownload(List<Album> albums, bool downloadCoverArt)
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
                    if (Globals.UserSettings.RetrieveFilesizes)
                        do
                        {
                            if (UserCanceled) return new List<TrackFile>();
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
                                if (tries < Globals.UserSettings.DownloadMaxTries)
                                    Log(
                                        $"Failed to retrieve the size of the cover art file for album \"{album.Title}\". Try {tries} of {Globals.UserSettings.DownloadMaxTries}",
                                        LogType.Warning);
                                else
                                    Log(
                                        $"Failed to retrieve the size of the cover art file for album \"{album.Title}\". Hit max retries of {Globals.UserSettings.DownloadMaxTries}. Progress update may be wrong.",
                                        LogType.Error);
                            }
                        } while (!sizeRetrieved && tries < Globals.UserSettings.DownloadMaxTries);

                    files.Add(new TrackFile(album.ArtworkUrl, 0, size));
                }

                // Tracks
                foreach (var track in album.Tracks)
                {
                    long size = 0;
                    var sizeRetrieved = false;
                    var tries = 0;
                    if (Globals.UserSettings.RetrieveFilesizes)
                        do
                        {
                            if (UserCanceled) return new List<TrackFile>();
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
                                if (tries < Globals.UserSettings.DownloadMaxTries)
                                    Log(
                                        $"Failed to retrieve the size of the MP3 file for the track \"{track.Title}\". Try {tries} of {Globals.UserSettings.DownloadMaxTries}",
                                        LogType.Warning);
                                else
                                    Log(
                                        $"Failed to retrieve the size of the MP3 file for the track \"{track.Title}\". Hit max retries of {Globals.UserSettings.DownloadMaxTries}. Progress update may be wrong.",
                                        LogType.Error);
                            }
                        } while (!sizeRetrieved && tries < Globals.UserSettings.DownloadMaxTries);

                    files.Add(new TrackFile(track.Mp3Url, 0, size));
                }
            }

            return files;
        }

        public void WaitForCooldown(int NumTries)
        {
            if (Globals.UserSettings.DownloadRetryCooldown != 0)
                Thread.Sleep((int)(Math.Pow(Globals.UserSettings.DownloadRetryExponential, NumTries) *
                                    Globals.UserSettings.DownloadRetryCooldown * 1000));
        }


        /// <summary>
        ///     Updates the progress messages and the progressbar.
        /// </summary>
        /// <param name="fileUrl">The URL of the file that just progressed.</param>
        /// <param name="bytesReceived">The received bytes for the specified file.</param>
        public void UpdateProgress(string fileUrl, long bytesReceived)
        {
            var now = DateTime.Now;

            lock (FilesDownload)
            {
                // Compute new progress values
                var currentFile = FilesDownload.Where(f => f.Url == fileUrl).First();
                currentFile.BytesReceived = bytesReceived;
                var totalReceivedBytes = FilesDownload.Sum(f => f.BytesReceived);
                var bytesToDownload = FilesDownload.Sum(f => f.Size);
                double downloadedFilesCount = FilesDownload.Count(f => f.Downloaded);

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
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Update download speed
                        DownloadSpeedString = (bytesPerSecond / 1024).ToString("0.0") + " kB/s";
                    });
                }

                // Update UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!UserCanceled)
                    {
                        if (progressReporter != null)
                        {
                            var args = new DownloadProgressReporter();
                            args.Text = ((double)totalReceivedBytes / (1024 * 1024)).ToString("0.00") + " MB" +
                                        (Globals.UserSettings.RetrieveFilesizes
                                            ? " / " + ((double)bytesToDownload / (1024 * 1024)).ToString("0.00") + " MB"
                                            : "");
                            if (Globals.UserSettings.RetrieveFilesizes)
                            {
                                // Update progress bar based on bytes received
                                args.Progress = totalReceivedBytes;
                                // Taskbar progress is between 0 and 1
                                if (maximumProgressValue != 0) args.ProgressBarValue = totalReceivedBytes / maximumProgressValue;
                            }
                            else
                            {
                                // Update progress bar based on downloaded files
                                args.Progress = (long)downloadedFilesCount;
                                // Taskbar progress is between 0 and count of files to download
                                args.ProgressBarValue = (long)(downloadedFilesCount / maximumProgressValue);

                            }

                            progressReporter.Report(args);


                        }


                    }
                });
            }
        }


        private void UpdateControlsState(bool update)
        {
            Messenger.Default.Send(new UpdateMainViewControlStateMessage(update));
        }

        private void Log(string eventMessage, LogType logType)
        {
            Messenger.Default.Send(new DownloaderLogMessage(eventMessage, logType));
        }

        private void PlayASound()
        {
            Messenger.Default.Send(new PlayASoundMessage(@"C:\Windows\Media\Windows Ding.wav"));
        }

        private void UpdateProgressNormal(bool isIndeterminate, long maxValue, TaskbarItemProgressState state)
        {
            Messenger.Default.Send(new ProgressUpdateMessage(isIndeterminate, maxValue, state));
        }

        #endregion
    }
}
