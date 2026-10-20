using InstaShare.FileManagers.Impl;

namespace InstaShare.Tests
{
    public class ShareKeysTests
    {
        [Theory]
        // Folder share: {guid}/{folder}
        [InlineData("bcbc25b7-7545-480f-ab41-512330032e34/InstaShare", "bcbc25b7-7545-480f-ab41-512330032e34/")]
        // File share: {name}-{guid}/{name}
        [InlineData("report.pdf-bcbc25b7-7545-480f-ab41-512330032e34/report.pdf", "report.pdf-bcbc25b7-7545-480f-ab41-512330032e34/")]
        // Names can contain dashes and slashes further down
        [InlineData("my-file-bcbc25b7-7545-480f-ab41-512330032e34/sub/my-file", "my-file-bcbc25b7-7545-480f-ab41-512330032e34/")]
        public void FindsShareFolder(string remoteId, string expected) => Assert.Equal(expected, ShareKeys.GetSharePrefix(remoteId));

        [Theory]
        [InlineData("")]
        [InlineData("/")]
        [InlineData("noslash")]
        [InlineData("photos/2024")]
        [InlineData("not-a-guid-at-all-but-long-enough-xxxxxxxx/file")]
        public void RejectsAnythingThatCouldDeleteOtherShares(string remoteId) =>
            Assert.Throws<ArgumentException>(() => ShareKeys.GetSharePrefix(remoteId));
    }
}
