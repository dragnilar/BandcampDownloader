using System;
using System.Collections.Generic;

namespace BandcampDownloader
{
    public class Album
    {
        public string Artist { get; set; }
        public string ArtworkUrl { get; set; }
        public DateTime ReleaseDate { get; set; }
        public List<Track> Tracks { get; set; }
        public string Title { get; set; }
    }
}