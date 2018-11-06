namespace BandcampDownloader.MediatorMessages
{
    public class UpdateMainViewControlStateMessage
    {
        public UpdateMainViewControlStateMessage(bool downloadStarted)
        {
            DownloadStarted = downloadStarted;
        }

        public bool DownloadStarted { get; set; }
    }
}