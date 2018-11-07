using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Config.Net;

namespace BandcampDownloader
{
    public static class Globals
    {
        public static IUserSettings UserSettings { get; set; } = new ConfigurationBuilder<IUserSettings>().UseIniFile(Constants.UserSettingsFilePath).Build();

        static Globals()
        {
            InitializeSettings(false);
        }

        public static void InitializeSettings(bool resetToDefaults)
        {
            if (resetToDefaults) File.Delete(Constants.UserSettingsFilePath);
            // Must set this before UI forms, cannot default in settings as it isn't determined by a constant function
            if (string.IsNullOrEmpty(UserSettings.DownloadsLocation))
                UserSettings.DownloadsLocation =
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\{artist}\\{album}";
                UserSettings = new ConfigurationBuilder<IUserSettings>().UseIniFile(Constants.UserSettingsFilePath).Build();
        }
    }
}
