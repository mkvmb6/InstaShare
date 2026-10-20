using InstaShare.FileManagers.Impl;
using InstaShare.Services;
using System.Collections.Concurrent;
using System.IO;

namespace InstaShare.Tests
{
    public sealed class SelectionTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InstaShare.Tests", Guid.NewGuid().ToString());

        public SelectionTests() => Directory.CreateDirectory(_dir);

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private string Touch(string relative)
        {
            var path = Path.Combine(_dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
            return path;
        }

        [Fact]
        public void FilesGoAtTheTopAndFoldersKeepTheirStructure()
        {
            var a = Touch(@"Holiday\a.jpg");
            var b = Touch(@"Holiday\b.jpg");
            Touch(@"Holiday\Raw\1.cr2");
            Touch(@"Holiday\Raw\Sub\2.cr2");

            var layout = ShareLayout.Arrange([a, b, Path.Combine(_dir, "Holiday", "Raw")]);

            Assert.Equal(["a.jpg", "b.jpg", "Raw/1.cr2", "Raw/Sub/2.cr2"], layout.Select(l => l.relativePath).Order());
            Assert.Equal(a, layout.Single(l => l.relativePath == "a.jpg").localPath);
        }

        [Fact]
        public void SameNameFromTwoPlacesIsNumberedNotOverwritten()
        {
            var first = Touch(@"One\report.pdf");
            var second = Touch(@"Two\report.pdf");
            var third = Touch(@"Three\report.pdf");

            var layout = ShareLayout.Arrange([first, second, third]);

            Assert.Equal(["report.pdf", "report (2).pdf", "report (3).pdf"], layout.Select(l => l.relativePath));
        }

        [Fact]
        public void GroupIsNamedAfterTheFolderTheFilesCameFrom()
        {
            Assert.Equal("Holiday", ShareLayout.GroupName([@"C:\Photos\Holiday\a.jpg", @"C:\Photos\Holiday\b.jpg"]));
            Assert.Equal("Shared files", ShareLayout.GroupName([@"C:\A\a.jpg", @"D:\B\b.jpg"]));
            Assert.Equal("Shared files", ShareLayout.GroupName([@"C:\a.jpg", @"C:\b.jpg"])); // A drive root has no name
        }

        [Fact]
        public void LaunchesArrivingTogetherBecomeOneBatch()
        {
            var batches = new ConcurrentQueue<IReadOnlyList<string>>();
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            using var batcher = new SelectionBatcher(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15), batches.Enqueue, () => now);

            batcher.Add(["a"]);
            now = now.AddMilliseconds(300);
            batcher.Add(["b"]);
            now = now.AddMilliseconds(300);
            batcher.Add(["c"]);
            batcher.Flush();

            var batch = Assert.Single(batches);
            Assert.Equal(["a", "b", "c"], batch);
        }

        [Fact]
        public async Task QuietTimeEndsTheBatch()
        {
            var batches = new ConcurrentQueue<IReadOnlyList<string>>();
            using var batcher = new SelectionBatcher(TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(15), batches.Enqueue);

            batcher.Add(["a"]);
            batcher.Add(["b"]);
            await WaitFor(() => batches.Count == 1);
            batcher.Add(["c"]);
            await WaitFor(() => batches.Count == 2);

            Assert.Equal([["a", "b"], ["c"]], batches.Select(b => b.ToArray()));
        }

        [Fact]
        public async Task AStreamOfLaunchesIsCutOffAtTheDeadline()
        {
            var batches = new ConcurrentQueue<IReadOnlyList<string>>();
            using var batcher = new SelectionBatcher(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200), batches.Enqueue);

            batcher.Add(["a"]);
            await Task.Delay(120);
            batcher.Add(["b"]); // Would push the quiet period to 5 s; the 200 ms deadline wins.

            await WaitFor(() => batches.Count == 1, TimeSpan.FromSeconds(2));
            Assert.Equal(["a", "b"], Assert.Single(batches));
        }

        private static async Task WaitFor(Func<bool> condition, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Condition not met in time.");
                await Task.Delay(20);
            }
        }
    }
}
