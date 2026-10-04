using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Muter
{
    internal static class Program
    {
        /// <summary>
        /// Uygulamanın ana girdi noktası.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (sender, args) =>
            {
                // Silently absorb COM / audio device disconnect exceptions to prevent annoying crash dialogs
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                // Safety net for background threads
            };

            Application.Run(new Form1());
        }
    }
}
