using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BandcampDownloader.MediatorMessages
{
    public class DownloaderLogMessage
    {
        public string LogEntry { get; }
        public LogType LogType { get; }

        public DownloaderLogMessage(string entry, LogType logType)
        {
            LogEntry = entry;
            LogType = logType;
        }
    }
}
