using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

// Hardware and UI tests share real devices, the registry and the screen: run everything sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Muter.Tests
{
    internal static class TestSetup
    {
        static TestSetup()
        {
            Log.Directory = Path.Combine(Path.GetTempPath(), "MuterTests-" + Guid.NewGuid().ToString("N"));
        }

        public static void Init()
        {
        }

        // Feedback sounds are not played during tests.
        public static void Silent(Muter.UI.FeedbackSound sound)
        {
        }

        // Runs an action on a fresh STA thread (WinForms and the production audio provider use STA).
        public static void RunSta(Action action)
        {
            Init();
            ExceptionDispatchInfo failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromMinutes(5)))
                throw new TimeoutException("STA test thread did not finish.");
            failure?.Throw();
        }

        // Pumps Windows messages until the condition holds or the timeout expires.
        public static bool PumpUntil(Func<bool> condition, int timeoutMs = 3000)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < end)
            {
                Application.DoEvents();
                if (condition())
                    return true;
                Thread.Sleep(10);
            }
            Application.DoEvents();
            return condition();
        }
    }
}
