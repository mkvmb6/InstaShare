using InstaShare.FileManagers.Impl;

namespace InstaShare.Tests
{
    public class TransferProgressTests
    {
        private const long MB = 1024 * 1024;

        private sealed class Harness
        {
            public long Now;
            public readonly List<(double percent, string? speed, long sent, TimeSpan? eta)> Reports = [];
            public readonly TransferProgress Tracker;

            public Harness(long totalBytes) =>
                Tracker = new TransferProgress(totalBytes, (p, s, b, e) => Reports.Add((p, s, b, e)), () => Now);

            public (double percent, string? speed, long sent, TimeSpan? eta) Last => Reports[^1];

            /// <summary>
            /// Sends <paramref name="bytesPerSecond"/> for <paramref name="ms"/>, in 100 ms steps.
            /// </summary>
            public void Run(long bytesPerSecond, long ms)
            {
                for (long t = 0; t < ms; t += 100)
                {
                    Now += 100;
                    // Exact running total, so the sum over a second is exactly bytesPerSecond.
                    Tracker.Add(bytesPerSecond * (t + 100) / 1000 - bytesPerSecond * t / 1000);
                }
            }
        }

        [Fact]
        public void NoSpeedUntilMeasured()
        {
            var h = new Harness(100 * MB);
            h.Run(MB, 400);
            Assert.All(h.Reports, r => Assert.Null(r.speed));
            Assert.All(h.Reports, r => Assert.Null(r.eta));

            h.Run(MB, 200);
            Assert.Equal("1.0 MB/s", h.Last.speed);
            Assert.NotNull(h.Last.eta);
        }

        [Fact]
        public void BarMovesEveryReportButSpeedOnlyOnItsRefresh()
        {
            var h = new Harness(1000 * MB);
            h.Run(MB, 1000);
            var before = h.Reports.Count;

            // Wildly uneven bursts: 9 MB in one tick, then nothing.
            for (var i = 0; i < 9; i++)
            {
                h.Now += 100;
                h.Tracker.Add(i == 0 ? 9 * MB : 0);
            }

            var during = h.Reports.Skip(before).ToList();
            Assert.Equal(9, during.Count);
            Assert.True(during[0].percent > h.Reports[before - 1].percent); // bar moved straight away
            // 9 ticks span 900 ms, so the text can change at most on its two 500 ms refreshes, not on every tick.
            Assert.True(during.Zip(during.Skip(1)).Count(p => p.First.speed != p.Second.speed) <= 2);
        }

        [Fact]
        public void SpeedIsAveragedOverSeconds()
        {
            var h = new Harness(1000 * MB);
            h.Run(2 * MB, 10_000);
            Assert.Equal("2.0 MB/s", h.Last.speed);

            // A stalled second shows as a dip, not a drop to 0. Depending on where the refresh lands,
            // the 5 s window has seen 0.5-1 s of the stall.
            h.Run(0, 1000);
            var speed = double.Parse(h.Last.speed!.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(speed, 1.5, 1.8);
        }

        [Fact]
        public void RetryRollbackDoesNotDropSpeedToZero()
        {
            var h = new Harness(100 * MB);
            h.Run(MB, 3000);
            h.Tracker.Add(-2 * MB); // A part failed and will be sent again
            h.Run(MB, 1000);

            Assert.Equal("1.0 MB/s", h.Last.speed);
            Assert.Equal(2 * MB, h.Last.sent);
        }

        [Fact]
        public void NoEtaWhenDone()
        {
            var h = new Harness(MB);
            h.Run(MB, 1000);
            h.Tracker.ReportNow();
            Assert.Equal(100, h.Last.percent);
            Assert.Null(h.Last.eta);
        }

        [Theory]
        [InlineData(50 * 1024.0, "50.0 KB/s")]
        [InlineData(820 * 1024.0, "820 KB/s")]
        [InlineData(8.24 * MB, "8.2 MB/s")]
        public void FormatsSpeed(double bytesPerSecond, string expected) => Assert.Equal(expected, TransferProgress.FormatSpeed(bytesPerSecond));
    }
}
