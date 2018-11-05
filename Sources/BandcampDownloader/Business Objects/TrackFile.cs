﻿using System;

namespace BandcampDownloader {
    public class TrackFile {
        public String  Url           { get; set; }
        public long    BytesReceived { get; set; }
        public long    Size          { get; set; }
        public Boolean Downloaded    { get; set; }

        public TrackFile(String url, long bytesReceived, long size) {
            this.Url           = url;
            this.BytesReceived = bytesReceived;
            this.Size          = size;
        }
    }
}