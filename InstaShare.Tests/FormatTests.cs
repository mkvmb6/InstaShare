using InstaShare.Services;
using InstaShare.ViewModels;

namespace InstaShare.Tests
{
    public class FormatTests
    {
        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(1023, "1023 B")]
        [InlineData(1536, "1.5 KB")]
        [InlineData(12L * 1024 * 1024 + 400 * 1024, "12.4 MB")]
        [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
        public void Bytes(long bytes, string expected) => Assert.Equal(expected, Format.Bytes(bytes));

        [Theory]
        [InlineData(0.2, "1 s")]
        [InlineData(45, "45 s")]
        [InlineData(200, "3 min 20 s")]
        [InlineData(3600, "1 h")]
        [InlineData(3900, "1 h 5 min")]
        [InlineData(86400 + 4 * 3600, "1 day 4 h")]
        [InlineData(2 * 86400, "2 days")]
        public void Duration(double seconds, string expected) => Assert.Equal(expected, Format.Duration(TimeSpan.FromSeconds(seconds)));

        [Fact]
        public void ExpiresIn_PastIsExpired()
        {
            var now = DateTime.UtcNow;
            Assert.Equal("Expired", Format.ExpiresIn(now.AddMinutes(-1), now));
            Assert.Equal("Expires in 1 day 4 h", Format.ExpiresIn(now.AddHours(28), now));
        }

        [Fact]
        public void ExpiryLabels() =>
            Assert.Equal(["1 hour", "6 hours", "12 hours", "1 day", "2 days", "7 days"], ExpiryOption.All.Select(o => o.Label));

        [Theory]
        [InlineData(48, 48)]
        [InlineData(50, 48)]
        [InlineData(0.5, 1)]
        [InlineData(5, 6)]
        [InlineData(10, 12)]
        [InlineData(1000, 168)]
        public void ClosestExpiry(double hours, double expected) => Assert.Equal(expected, ExpiryOption.Closest(hours).Value.TotalHours);
    }
}
