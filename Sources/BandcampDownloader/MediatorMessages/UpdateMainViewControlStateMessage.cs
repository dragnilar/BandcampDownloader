using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BandcampDownloader.MediatorMessages
{
    public class UpdateMainViewControlStateMessage
    {
        public bool DownloadStarted { get; set; }

        public UpdateMainViewControlStateMessage(bool downloadStarted)
        {
            DownloadStarted = downloadStarted;
        }
    }
}
