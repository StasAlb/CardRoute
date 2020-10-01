using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DpclDevice
{
    public enum PrinterStatus
    {
        /// <summary>
        /// The printer has an unknown status
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The printer is disconnected
        /// </summary>
        Off = 1,

        /// <summary>
        /// The printer is idle
        /// </summary>
        Ready = 2,

        /// <summary>
        /// The printer is busy
        /// </summary>
        Busy = 3,

        /// <summary>
        /// The printer is suspended
        /// </summary>
        Suspended = 4
    }
    public class EmbossString
    {
        public int x;
        public int y;
        public int font;
        public string text;

        public EmbossString(int X, int Y, int Font, string Text)
        {
            x = X;
            y = Y;
            font = Font;
            text = Text;
        }
        public EmbossString() { }
    }

}
