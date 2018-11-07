using Config.Net;

namespace BandcampDownloader
{
    public interface IUserSettings
    {
        [Option(DefaultValue = true)] bool ConvertCoverArtToJpg { get; set; }

        [Option(DefaultValue = 1000)] int CoverArtMaxSize { get; set; }

        [Option(DefaultValue = false)] bool DownloadOneAlbumAtATime { get; set; }

        [Option(DefaultValue = "")] string DownloadsLocation { get; set; }

        [Option(DefaultValue = false)] bool DownloadArtistDiscography { get; set; }

        [Option(DefaultValue = true)] bool ResizeCoverArt { get; set; }

        [Option(DefaultValue = false)] bool SaveCoverArtInFolder { get; set; }

        [Option(DefaultValue = true)] bool SaveCoverArtInTags { get; set; }

        [Option(DefaultValue = false)] bool ShowVerboseLog { get; set; }

        [Option(DefaultValue = true)] bool TagTracks { get; set; }

        [Option(DefaultValue = "{tracknum} {artist} - {title}.mp3")]
        string FilenameFormat { get; set; }

        // Annotation required to allow serialization of static field
        [Option(DefaultValue = 7)] int DownloadMaxTries { get; set; }

        // Time in seconds between retries
        [Option(DefaultValue = 0.2)] double DownloadRetryCooldown { get; set; }

        // Exponential per cooldown - ex. (value of 1.2 would yield cooldowns of x^(1.2^0), x^(1.2^1), x^(1.2^2), ..)
        [Option(DefaultValue = 4.0)] double DownloadRetryExponential { get; set; }

        [Option(DefaultValue = 0.05)] double AllowableFileSizeDifference { get; set; }

        [Option(DefaultValue = true)] bool RetrieveFilesizes { get; set; }

        [Option(DefaultValue = true)] bool AutoScrollLog { get; set; }
    }
}