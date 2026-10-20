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

        [Theory]
        [InlineData("bcbc25b7-7545-480f-ab41-512330032e34/My Photos", "/view/bcbc25b7-7545-480f-ab41-512330032e34/My%20Photos")]
        [InlineData("Q3 report.pdf-bcbc25b7-7545-480f-ab41-512330032e34", "/view/Q3%20report.pdf-bcbc25b7-7545-480f-ab41-512330032e34")]
        [InlineData("bcbc25b7-7545-480f-ab41-512330032e34/R&D #1?", "/view/bcbc25b7-7545-480f-ab41-512330032e34/R%26D%20%231%3F")]
        public void LinkHasNoSpaces(string sharePath, string expectedPath) =>
            Assert.Equal(Constants.AppBaseUrl[..^"/view".Length] + expectedPath, ShareKeys.GetShareLink(sharePath));

        [Theory]
        [InlineData("https://example.test/view/abc/My Photos", "https://example.test/view/abc/My%20Photos")]
        [InlineData("https://example.test/view/abc/My%20Photos", "https://example.test/view/abc/My%20Photos")]
        public void NormalizesOldLinks(string link, string expected) => Assert.Equal(expected, ShareKeys.NormalizeLink(link));
    }
}
