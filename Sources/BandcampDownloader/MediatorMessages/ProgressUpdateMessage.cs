using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shell;

namespace BandcampDownloader.MediatorMessages
{
    public class ProgressUpdateMessage
    {
        public bool IsIndeterminate { get; set; }
        public long Maximum { get; set; }
        public TaskbarItemProgressState ProgressState { get; set; }

        public ProgressUpdateMessage(bool isIndeterminate, long maximum, TaskbarItemProgressState progressState)
        {
            IsIndeterminate = isIndeterminate;
            Maximum = maximum;
            ProgressState = progressState;
        }
    }
}
