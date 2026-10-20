using InstaShare.FileManagers.Impl;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InstaShare.Tests
{
    public sealed class PreviewRendererTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InstaShare.Tests", Guid.NewGuid().ToString());

        public PreviewRendererTests() => Directory.CreateDirectory(_dir);

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        [Fact]
        public void PicksImagesFirstThenVideosThenPdfs_ShallowBeforeDeep()
        {
            var picked = PreviewRenderer.PickCandidates([
                ("c:\\a\\report.pdf", "Trip/report.pdf"),
                ("c:\\a\\notes.txt", "Trip/notes.txt"),
                ("c:\\a\\clip.MP4", "Trip/clip.MP4"),
                ("c:\\a\\deep.jpg", "Trip/Sub/deep.jpg"),
                ("c:\\a\\b.PNG", "Trip/b.PNG"),
                ("c:\\a\\a.heic", "Trip/a.heic"),
            ]);

            Assert.Equal(["Trip/a.heic", "Trip/b.PNG", "Trip/Sub/deep.jpg", "Trip/clip.MP4", "Trip/report.pdf"], picked.Select(p => p.SharePath));
            Assert.Equal([PreviewKind.Image, PreviewKind.Image, PreviewKind.Image, PreviewKind.Video, PreviewKind.Pdf], picked.Select(p => p.Kind));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void CollageTilesFillTheCardWithoutOverlapping(int count)
        {
            var rects = PreviewRenderer.CollageLayout(count, 1200, 630, 6);

            Assert.Equal(count, rects.Count);
            Assert.All(rects, r => Assert.True(r.Left >= 0 && r.Top >= 0 && r.Right <= 1200 && r.Bottom <= 630));
            for (var i = 0; i < rects.Count; i++)
                for (var j = i + 1; j < rects.Count; j++)
                    Assert.False(rects[i].IntersectsWith(Shrink(rects[j])), $"tiles {i} and {j} overlap");
            // Tiles plus gaps cover the whole card.
            var area = rects.Sum(r => r.Width * r.Height);
            Assert.InRange(area / (1200 * 630), 0.97, 1.0);
        }

        [Theory]
        [InlineData(1, 400, 200)]
        [InlineData(3, 400, 200)]
        [InlineData(6, 200, 400)]  // Phone held upright: stored sideways, shown upright
        [InlineData(8, 200, 400)]
        public void TurnsPhotosUpright(int orientation, int expectedWidth, int expectedHeight)
        {
            var oriented = PreviewRenderer.Orient(Solid(400, 200, Colors.Red), orientation);
            Assert.Equal((expectedWidth, expectedHeight), (oriented.PixelWidth, oriented.PixelHeight));
        }

        [Fact]
        public async Task SingleImageKeepsItsShapeAndIsMadeSmall()
        {
            var big = WritePng("big.png", 3000, 2000, Colors.SteelBlue);

            var preview = await PreviewRenderer.RenderAsync(PreviewRenderer.PickCandidates([(big, "big.png")]), CancellationToken.None);

            Assert.NotNull(preview);
            Assert.Equal((1200, 800), (preview.Width, preview.Height));
            AssertJpeg(preview);
        }

        [Fact]
        public async Task SeveralImagesBecomeACollageCard()
        {
            var files = Enumerable.Range(1, 6).Select(i => (WritePng($"{i}.png", 800, 600, Color.FromRgb((byte)(i * 40), 90, 160)), $"Trip/{i}.png"));

            var preview = await PreviewRenderer.RenderAsync(PreviewRenderer.PickCandidates(files), CancellationToken.None);

            Assert.NotNull(preview);
            Assert.Equal((PreviewRenderer.CardWidth, PreviewRenderer.CardHeight), (preview.Width, preview.Height));
            AssertJpeg(preview);
            Assert.InRange(preview.Jpeg.Length, 1, 600 * 1024);
        }

        [Fact]
        public async Task PdfShowsItsFirstPage()
        {
            var pdf = Path.Combine(_dir, "doc.pdf");
            File.WriteAllBytes(pdf, MinimalPdf());

            var preview = await PreviewRenderer.RenderAsync(PreviewRenderer.PickCandidates([(pdf, "doc.pdf")]), CancellationToken.None);

            Assert.NotNull(preview);
            Assert.Equal((PreviewRenderer.CardWidth, PreviewRenderer.CardHeight), (preview.Width, preview.Height));
        }

        [Fact]
        public async Task NothingDecodableMeansNoPreview()
        {
            var broken = Path.Combine(_dir, "broken.jpg");
            File.WriteAllText(broken, "not really a jpeg");
            var brokenPdf = Path.Combine(_dir, "broken.pdf");
            File.WriteAllText(brokenPdf, "%PDF-1.4 garbage");

            Assert.Null(await PreviewRenderer.RenderAsync(PreviewRenderer.PickCandidates([(broken, "broken.jpg"), (brokenPdf, "broken.pdf")]), CancellationToken.None));
            Assert.Null(await PreviewRenderer.RenderAsync([], CancellationToken.None));
        }

        [Fact]
        public async Task SkipsFilesThatWontDecodeAndUsesTheRest()
        {
            var broken = Path.Combine(_dir, "a-broken.jpg");
            File.WriteAllText(broken, "nope");
            var good = WritePng("b-good.png", 640, 480, Colors.Orange);

            var preview = await PreviewRenderer.RenderAsync(PreviewRenderer.PickCandidates([(broken, "a-broken.jpg"), (good, "b-good.png")]), CancellationToken.None);

            Assert.NotNull(preview);
            Assert.Equal((640, 480), (preview.Width, preview.Height));
        }

        private static Rect Shrink(Rect r) => new(r.X + 0.5, r.Y + 0.5, Math.Max(0, r.Width - 1), Math.Max(0, r.Height - 1));

        private static void AssertJpeg(RenderedPreview preview) =>
            Assert.True(preview.Jpeg is [0xFF, 0xD8, ..], "not a JPEG");

        private static BitmapSource Solid(int width, int height, Color color)
        {
            var pixels = new byte[width * height * 4];
            for (var i = 0; i < pixels.Length; i += 4)
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (color.B, color.G, color.R, 255);
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            return bitmap;
        }

        private string WritePng(string name, int width, int height, Color color)
        {
            var path = Path.Combine(_dir, name);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Solid(width, height, color)));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }

        /// <summary>
        /// A one-page PDF with a filled rectangle, with a correct cross-reference table.
        /// </summary>
        private static byte[] MinimalPdf()
        {
            const string content = "0.2 0.4 0.8 rg 72 600 300 150 re f";
            string[] objects =
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << >> >>",
                $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            ];
            var pdf = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (var i = 0; i < objects.Length; i++)
            {
                offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
                pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }
            var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
            pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets)
                pdf.Append($"{offset:D10} 00000 n \n");
            pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return Encoding.ASCII.GetBytes(pdf.ToString());
        }
    }
}
