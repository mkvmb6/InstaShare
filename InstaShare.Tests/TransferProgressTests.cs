using InstaShare.FileManagers.Impl;

namespace InstaShare.Tests
{
    public class TransferProgressTests
    {
        [Fact]
        public void ReportsBytesAndEta()
        {
            (double percent, long sent, TimeSpan? eta)? last = null;
            var tracker = new TransferProgress(10_000_000, (percent, _, sent, eta) => last = (percent, sent, eta));

            Thread.Sleep(150);
            tracker.Add(1_000_000);
            tracker.ReportNow();

            Assert.NotNull(last);
            Assert.Equal(1_000_000, last.Value.sent);
            Assert.Equal(10, last.Value.percent, precision: 1);
            Assert.NotNull(last.Value.eta);
            Assert.True(last.Value.eta > TimeSpan.Zero);
        }

        [Fact]
        public void NoEtaWhenDone()
        {
            TimeSpan? eta = TimeSpan.MaxValue;
            var tracker = new TransferProgress(100, (_, _, _, e) => eta = e);
            Thread.Sleep(150);
            tracker.Add(100);
            tracker.ReportNow();
            Assert.Null(eta);
        }
    }
}
