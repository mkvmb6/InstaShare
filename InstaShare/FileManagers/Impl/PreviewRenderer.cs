using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPdf = Windows.Data.Pdf;
using WinProps = Windows.Storage.FileProperties;
using WinStorage = Windows.Storage;
using WinStreams = Windows.Storage.Streams;

namespace InstaShare.FileManagers.Impl
{
    internal sealed record RenderedPreview(byte[] Jpeg, int Width, int Height);

    // Order matters: it's the preference when picking what goes in the picture.
    internal enum PreviewKind { Image, Video, Pdf }

    internal sealed record PreviewCandidate(string LocalPath, string SharePath, PreviewKind Kind);

    /// <summary>
    /// The picture chat apps show for a share link: the image itself, a video frame, a PDF's first page,
    /// or a collage when there are several. Made from the local files, so it's ready long before the upload is.
    /// </summary>
    internal static class PreviewRenderer
    {
        public const int CardWidth = 1200;
        public const int CardHeight = 630;
        private const int MaxImageSide = 1200;
        private const double Gap = 6;
        private const int MaxTiles = 4;
        private const int MaxCandidates = 6; // A couple of spares for files that won't decode
        private const int DecodeSide = 1600;
        private const long MaxImageBytes = 50L * 1024 * 1024;
        private const int JpegQuality = 82;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".webp", ".heic", ".heif", ".tif", ".tiff" };
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".wmv", ".webm", ".3gp", ".mpg", ".mpeg" };

        private static readonly Color CollageBackground = Color.FromRgb(0x0f, 0x17, 0x2a);
        private static readonly Color PageBackground = Color.FromRgb(0xe5, 0xe7, 0xeb);

        public static PreviewKind? KindOf(string path)
        {
            var extension = Path.GetExtension(path);
            if (ImageExtensions.Contains(extension))
                return PreviewKind.Image;
            if (VideoExtensions.Contains(extension))
                return PreviewKind.Video;
            return string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) ? PreviewKind.Pdf : null;
        }

        /// <summary>
        /// Everything in the share that can be pictured: images first, then videos, then PDFs;
        /// shallow files before deep ones, then by name.
        /// </summary>
        public static IReadOnlyList<PreviewCandidate> PickCandidates(IEnumerable<(string localPath, string sharePath)> files) =>
            [.. files
                .Select(f => (f.localPath, f.sharePath, kind: KindOf(f.localPath)))
                .Where(f => f.kind != null)
                .Select(f => new PreviewCandidate(f.localPath, f.sharePath, f.kind!.Value))
                .OrderBy(c => c.Kind)
                .ThenBy(c => c.SharePath.Count(ch => ch == '/'))
                .ThenBy(c => c.SharePath, StringComparer.OrdinalIgnoreCase)];

        /// <returns>The preview, or null when nothing in the share can be pictured. Never throws.</returns>
        public static async Task<RenderedPreview?> RenderAsync(IReadOnlyList<PreviewCandidate> candidates, CancellationToken cancellationToken)
        {
            if (candidates.Count == 0)
                return null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Timeout);
                var picked = candidates.Take(MaxCandidates).ToList();

                // Video frames and PDF pages come from Windows' own async renderers; images are decoded below.
                var thumbnails = await Task.WhenAll(picked.Select(c =>
                    c.Kind == PreviewKind.Image ? Task.FromResult<byte[]?>(null) : TryGetThumbnail(c, timeout.Token)));
                cancellationToken.ThrowIfCancellationRequested();

                // WPF drawing wants an STA thread; inputs cross over as plain bytes and paths.
                return await RunSta(() => Compose(picked, thumbnails, candidates.Count));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Tile positions for a collage of <paramref name="count"/> pictures: side by side, one big plus two, or a 2×2 grid.
        /// </summary>
        public static IReadOnlyList<Rect> CollageLayout(int count, double width, double height, double gap)
        {
            var halfW = (width - gap) / 2;
            var halfH = (height - gap) / 2;
            var right = halfW + gap;
            var bottom = halfH + gap;
            return count switch
            {
                <= 1 => [new Rect(0, 0, width, height)],
                2 => [new Rect(0, 0, halfW, height), new Rect(right, 0, halfW, height)],
                3 => [new Rect(0, 0, halfW, height), new Rect(right, 0, halfW, halfH), new Rect(right, bottom, halfW, halfH)],
                _ => [new Rect(0, 0, halfW, halfH), new Rect(right, 0, halfW, halfH), new Rect(0, bottom, halfW, halfH), new Rect(right, bottom, halfW, halfH)],
            };
        }

        /// <summary>
        /// Turns a photo upright according to its EXIF orientation (1-8). Cameras and phones often store the
        /// pixels sideways and only record the rotation, so without this previews come out on their side.
        /// </summary>
        public static BitmapSource Orient(BitmapSource source, int orientation)
        {
            Transform? transform = orientation switch
            {
                2 => new ScaleTransform(-1, 1),
                3 => new RotateTransform(180),
                4 => new ScaleTransform(1, -1),
                5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
                6 => new RotateTransform(90),
                7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
                8 => new RotateTransform(270),
                _ => null,
            };
            if (transform == null)
                return source;
            var oriented = new TransformedBitmap(source, transform);
            oriented.Freeze();
            return oriented;
        }

        private static async Task<byte[]?> TryGetThumbnail(PreviewCandidate candidate, CancellationToken cancellationToken)
        {
            try
            {
                var file = await WinStorage.StorageFile.GetFileFromPathAsync(candidate.LocalPath).AsTask(cancellationToken);
                if (candidate.Kind == PreviewKind.Video)
                {
                    // The same frame Explorer shows. Without a codec Windows hands back the file-type icon instead, which is no preview.
                    using var thumbnail = await file.GetThumbnailAsync(WinProps.ThumbnailMode.SingleItem, 1024, WinProps.ThumbnailOptions.ResizeThumbnail).AsTask(cancellationToken);
                    if (thumbnail == null || thumbnail.Type != WinProps.ThumbnailType.Image)
                        return null;
                    return await ReadAll(thumbnail, cancellationToken);
                }

                // Password-protected or damaged PDFs throw here, and simply get no preview.
                var pdf = await WinPdf.PdfDocument.LoadFromFileAsync(file).AsTask(cancellationToken);
                if (pdf.PageCount == 0)
                    return null;
                using var page = pdf.GetPage(0);
                using var stream = new WinStreams.InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, new WinPdf.PdfPageRenderOptions { DestinationHeight = 900 }).AsTask(cancellationToken);
                return await ReadAll(stream, cancellationToken);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static async Task<byte[]> ReadAll(WinStreams.IRandomAccessStream source, CancellationToken cancellationToken)
        {
            source.Seek(0);
            using var stream = source.AsStreamForRead();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }

        private static RenderedPreview? Compose(IReadOnlyList<PreviewCandidate> picked, byte[]?[] thumbnails, int total)
        {
            var tiles = new List<(BitmapSource bitmap, PreviewKind kind)>();
            for (var i = 0; i < picked.Count && tiles.Count < MaxTiles; i++)
            {
                var bitmap = picked[i].Kind == PreviewKind.Image ? TryLoadImage(picked[i].LocalPath) : TryDecode(thumbnails[i]);
                if (bitmap != null)
                    tiles.Add((bitmap, picked[i].Kind));
            }
            if (tiles.Count == 0)
                return null;

            var card = new Rect(0, 0, CardWidth, CardHeight);
            if (tiles.Count == 1)
            {
                var (bitmap, kind) = tiles[0];
                return kind switch
                {
                    PreviewKind.Image => RenderImage(bitmap),
                    PreviewKind.Video => Render(CardWidth, CardHeight, dc => { DrawCover(dc, bitmap, card); DrawPlay(dc, card); }),
                    _ => Render(CardWidth, CardHeight, dc => DrawPage(dc, bitmap, card)),
                };
            }

            // Everything in the share that isn't on a tile, including files that wouldn't decode.
            var more = total - tiles.Count;
            return Render(CardWidth, CardHeight, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(CollageBackground), null, card);
                var rects = CollageLayout(tiles.Count, CardWidth, CardHeight, Gap);
                for (var i = 0; i < tiles.Count; i++)
                {
                    var (bitmap, kind) = tiles[i];
                    if (kind == PreviewKind.Pdf)
                        DrawPage(dc, bitmap, rects[i]);
                    else
                        DrawCover(dc, bitmap, rects[i]);
                    if (kind == PreviewKind.Video)
                        DrawPlay(dc, rects[i]);
                }
                if (more > 0)
                    DrawMore(dc, rects[^1], more);
            });
        }

        /// <summary>
        /// A single image keeps its own shape; it's only made smaller.
        /// </summary>
        private static RenderedPreview RenderImage(BitmapSource bitmap)
        {
            var scale = Math.Min(1.0, (double)MaxImageSide / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
            var width = Math.Max(1, (int)Math.Round(bitmap.PixelWidth * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.PixelHeight * scale));
            var rect = new Rect(0, 0, width, height);
            return Render(width, height, dc =>
            {
                // JPEG has no transparency; white keeps transparent PNGs looking as intended.
                dc.DrawRectangle(Brushes.White, null, rect);
                dc.DrawImage(bitmap, rect);
            });
        }

        private static void DrawCover(DrawingContext dc, BitmapSource bitmap, Rect rect)
        {
            var scale = Math.Max(rect.Width / bitmap.PixelWidth, rect.Height / bitmap.PixelHeight);
            DrawCentered(dc, bitmap, rect, scale, clip: true);
        }

        /// <summary>
        /// A document page, whole and centred, on a light background with a soft shadow.
        /// </summary>
        private static void DrawPage(DrawingContext dc, BitmapSource bitmap, Rect rect)
        {
            dc.DrawRectangle(new SolidColorBrush(PageBackground), null, rect);
            var pad = rect.Height * 0.06;
            var inner = new Rect(rect.X + pad, rect.Y + pad, Math.Max(1, rect.Width - 2 * pad), Math.Max(1, rect.Height - 2 * pad));
            var scale = Math.Min(inner.Width / bitmap.PixelWidth, inner.Height / bitmap.PixelHeight);
            var size = new Size(bitmap.PixelWidth * scale, bitmap.PixelHeight * scale);
            var page = new Rect(inner.X + (inner.Width - size.Width) / 2, inner.Y + (inner.Height - size.Height) / 2, size.Width, size.Height);
            var shadow = page;
            shadow.Offset(0, Math.Max(2, pad * 0.15));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, shadow);
            dc.DrawRectangle(Brushes.White, null, page);
            dc.DrawImage(bitmap, page);
        }

        private static void DrawCentered(DrawingContext dc, BitmapSource bitmap, Rect rect, double scale, bool clip)
        {
            var width = bitmap.PixelWidth * scale;
            var height = bitmap.PixelHeight * scale;
            if (clip)
                dc.PushClip(new RectangleGeometry(rect));
            dc.DrawImage(bitmap, new Rect(rect.X + (rect.Width - width) / 2, rect.Y + (rect.Height - height) / 2, width, height));
            if (clip)
                dc.Pop();
        }

        private static void DrawPlay(DrawingContext dc, Rect rect)
        {
            var radius = Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.11, 16, 56);
            var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), new Pen(Brushes.White, radius * 0.08), center, radius, radius);

            var triangle = new StreamGeometry();
            using (var g = triangle.Open())
            {
                g.BeginFigure(new Point(center.X - radius * 0.3, center.Y - radius * 0.45), isFilled: true, isClosed: true);
                g.LineTo(new Point(center.X - radius * 0.3, center.Y + radius * 0.45), isStroked: false, isSmoothJoin: false);
                g.LineTo(new Point(center.X + radius * 0.5, center.Y), isStroked: false, isSmoothJoin: false);
            }
            dc.DrawGeometry(Brushes.White, null, triangle);
        }

        private static void DrawMore(DrawingContext dc, Rect rect, int more)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), null, rect);
            var text = new FormattedText($"+{more}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                rect.Height * 0.28, Brushes.White, pixelsPerDip: 1.0);
            dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
        }

        private static RenderedPreview Render(int width, int height, Action<DrawingContext> draw)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
                draw(dc);

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);

            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var output = new MemoryStream();
            encoder.Save(output);
            return new RenderedPreview(output.ToArray(), width, height);
        }

        private static BitmapSource? TryLoadImage(string path)
        {
            try
            {
                if (new FileInfo(path).Length > MaxImageBytes)
                    return null;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                // Size and rotation come from the header; the pixels are decoded once, already scaled down.
                var header = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                var (width, height) = (header.PixelWidth, header.PixelHeight);
                var orientation = ReadOrientation(header);

                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (Math.Max(width, height) > DecodeSide)
                {
                    if (width >= height)
                        image.DecodePixelWidth = DecodeSide;
                    else
                        image.DecodePixelHeight = DecodeSide;
                }
                image.EndInit();
                image.Freeze();
                return Orient(image, orientation);
            }
            catch (Exception)
            {
                // Unsupported format (HEIC without Windows' HEIF extension), damaged, or locked.
                return null;
            }
        }

        private static int ReadOrientation(BitmapFrame frame)
        {
            try
            {
                if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("System.Photo.Orientation")
                    && metadata.GetQuery("System.Photo.Orientation") is ushort orientation)
                    return orientation;
            }
            catch (Exception)
            {
                // Formats without EXIF (PNG, GIF) can throw on metadata queries.
            }
            return 1;
        }

        private static BitmapSource? TryDecode(byte[]? bytes)
        {
            if (bytes == null)
                return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = new MemoryStream(bytes);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Task<T> RunSta<T>(Func<T> work)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    result.SetResult(work());
                }
                catch (Exception ex)
                {
                    result.SetException(ex);
                }
            })
            { IsBackground = true, Name = "InstaShare preview" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return result.Task;
        }
    }
}
