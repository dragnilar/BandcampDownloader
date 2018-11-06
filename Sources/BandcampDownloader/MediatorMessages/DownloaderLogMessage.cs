namespace BandcampDownloader.MediatorMessages
{
    public class DownloaderLogMessage
    {
        public DownloaderLogMessage(string entry, LogType logType)
        {
            LogEntry = entry;
            LogType = logType;
        }

        public string LogEntry { get; }
        public LogType LogType { get; }
    }
}