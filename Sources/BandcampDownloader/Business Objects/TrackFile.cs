namespace BandcampDownloader
{
    public class TrackFile
    {
        public TrackFile(string url, long bytesReceived, long size)
        {
            Url = url;
            BytesReceived = bytesReceived;
            Size = size;
        }

        public string Url { get; set; }
        public long BytesReceived { get; set; }
        public long Size { get; set; }
        public bool Downloaded { get; set; }
    }
}