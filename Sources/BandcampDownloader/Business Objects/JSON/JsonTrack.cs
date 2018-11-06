﻿using Newtonsoft.Json;

namespace BandcampDownloader
{
    internal class JsonTrack
    {
        [JsonProperty("file")] public JsonMp3File File { get; set; }

        [JsonProperty("title")] public string Title { get; set; }

        [JsonProperty("track_num")] public int Number { get; set; }

        [JsonProperty("lyrics")] public string Lyrics { get; set; }

        public Track ToTrack()
        {
            return new Track
            {
                Mp3Url = (File.Url.StartsWith("//") ? "http:" : "") + File.Url, // "//example.com" Uri lacks protocol
                Number = Number == 0 ? 1 : Number, // For bandcamp track pages, Number will be 0. Set 1 instead
                Title = Title,
                Lyrics = Lyrics
            };
        }
    }
}