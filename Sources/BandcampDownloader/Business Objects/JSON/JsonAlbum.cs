using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace BandcampDownloader
{
    internal class JsonAlbum
    {
        public string urlEnd = "_10.jpg";

        // This uses the art_id variable to retrieve the image from the new bandcamp hosting site
        public string urlStart = "https://f4.bcbits.com/img/a";

        [JsonProperty("artist")] public string Artist { get; set; }

        [JsonProperty("art_id")] public string artId { get; set; }

        [JsonProperty("album_release_date")] public DateTime ReleaseDate { get; set; }

        [JsonProperty("trackinfo")] public List<JsonTrack> Tracks { get; set; }

        [JsonProperty("current")] public JsonAlbumData AlbumData { get; set; }

        public Album ToAlbum()
        {
            return new Album
            {
                Artist = Artist,
                ArtworkUrl = urlStart + artId.PadLeft(10, '0') + urlEnd,
                ReleaseDate = ReleaseDate,
                Title = AlbumData.AlbumTitle,
                // Some tracks do not have their URL filled on some albums (pre-release...)
                // Forget those tracks here
                Tracks = Tracks.Where(t => t.File != null).Select(t => t.ToTrack()).ToList()
            };
        }
    }
}