using System;
using System.Windows.Media;
using Color = System.Drawing.Color;

namespace BandcampDownloader
{
    internal static class LogHelper
    {
        /// <summary>
        ///     Returns the color associated to the specified log type.
        /// </summary>
        /// <param name="logType">The type of the log.</param>
        public static Color GetColor(LogType logType)
        {
            SolidColorBrush brush;

            switch (logType)
            {
                case LogType.Info:
                    brush = Brushes.Black;
                    break;
                case LogType.VerboseInfo:
                    brush = Brushes.MediumBlue;
                    break;
                case LogType.IntermediateSuccess:
                    brush = Brushes.MediumBlue;
                    break;
                case LogType.Success:
                    brush = Brushes.Green;
                    break;
                case LogType.Warning:
                    brush = Brushes.OrangeRed;
                    break;
                case LogType.Error:
                    brush = Brushes.Red;
                    break;
                default:
                    throw new NotImplementedException();
            }

            var color = Color.FromArgb(brush.Color.A, brush.Color.B, brush.Color.G, brush.Color.B);

            return color;

        }
    }
}