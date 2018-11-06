using System.Windows.Shell;

namespace BandcampDownloader.MediatorMessages
{
    public class ProgressUpdateMessage
    {
        public ProgressUpdateMessage(bool isIndeterminate, long maximum, TaskbarItemProgressState progressState)
        {
            IsIndeterminate = isIndeterminate;
            Maximum = maximum;
            ProgressState = progressState;
        }

        public bool IsIndeterminate { get; set; }
        public long Maximum { get; set; }
        public TaskbarItemProgressState ProgressState { get; set; }
    }
}