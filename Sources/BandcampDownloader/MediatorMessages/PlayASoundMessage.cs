using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BandcampDownloader.MediatorMessages
{
    public class PlayASoundMessage
    {
        public string SoundFileString { get; set; }

        public PlayASoundMessage(string soundFileString)
        {
            SoundFileString = soundFileString;
        }
    }
}
