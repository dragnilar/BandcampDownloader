using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BandcampDownloader.ProgressReporters
{
    public class DownloadProgressReporter
    {
        public long Progress { get; set; }
        public long ProgressBarValue { get; set; }
        public string Text { get; set; }
    }
}
