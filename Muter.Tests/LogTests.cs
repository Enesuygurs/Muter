using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Muter.Tests
{
    public sealed class LogTests : IDisposable
    {
        private readonly string previous;
        private readonly string dir = Path.Combine(Path.GetTempPath(), "MuterLogTest-" + Guid.NewGuid().ToString("N"));

        public LogTests()
        {
            TestSetup.Init();
            previous = Log.Directory;
            Log.Directory = dir;
        }

        public void Dispose()
        {
            Log.Directory = previous;
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }

        [Fact]
        public void Every_info_line_is_kept_but_repeated_warnings_are_suppressed()
        {
            for (int i = 0; i < 3; i++)
                Log.Info("Microphone state: Live -> Muted");
            for (int i = 0; i < 3; i++)
                Log.Warn("Could not read", new InvalidOperationException("boom"));
            Log.Warn("Could not read", new InvalidOperationException("different"));

            string[] lines = File.ReadAllLines(Log.FilePath);
            Assert.Equal(3, lines.Count(l => l.Contains("[INFO] Microphone state")));
            Assert.Equal(1, lines.Count(l => l.Contains("boom")));
            Assert.Equal(1, lines.Count(l => l.Contains("different")));
        }

        [Fact]
        public void Rotates_at_one_megabyte()
        {
            string text = new string('x', 2000);
            for (int i = 0; i < 600; i++)
                Log.Info(text + i);

            Assert.True(File.Exists(Path.Combine(dir, "muter.old.log")));
            Assert.True(new FileInfo(Log.FilePath).Length <= 1024 * 1024 + 4096);
        }

        [Fact]
        public void Never_throws_even_if_the_directory_is_unusable()
        {
            Log.Directory = "Z:\\does\\not\\exist\\" + new string('?', 10);
            Log.Error("still fine", new Exception("x"));
        }
    }
}
