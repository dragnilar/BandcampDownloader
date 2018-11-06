namespace BandcampDownloader.MediatorMessages
{
    public class PlayASoundMessage
    {
        public PlayASoundMessage(string soundFileString)
        {
            SoundFileString = soundFileString;
        }

        public string SoundFileString { get; set; }
    }
}