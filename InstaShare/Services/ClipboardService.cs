using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InstaShare.Services
{
    internal static class ClipboardService
    {
        public static bool TrySetText(string text)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (Exception)
            {
                // Another app can hold the clipboard open.
                return false;
            }
        }

        /// <summary>
        /// Turns what's on the clipboard into paths to share: copied files as they are, and an image or
        /// text saved to a temp file first.
        /// </summary>
        public static IReadOnlyList<string> GetShareablePaths()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                    return [.. Clipboard.GetFileDropList().Cast<string>()];

                var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss");
                if (Clipboard.GetData("PNG") is MemoryStream png)
                    return [WriteTemp($"Screenshot {stamp}.png", stream => png.CopyTo(stream))];

                if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
                {
                    // Bitmaps from the clipboard often carry an all-zero alpha channel; drop it so the PNG isn't transparent.
                    var opaque = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
                    return [WriteTemp($"Screenshot {stamp}.png", stream =>
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(opaque));
                        encoder.Save(stream);
                    })];
                }

                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                        return [WriteTemp($"Clipboard {stamp}.txt", stream => stream.Write(System.Text.Encoding.UTF8.GetBytes(text)))];
                }
            }
            catch (Exception)
            {
                // Clipboard busy or holding data we can't read; treat it as empty.
            }
            return [];
        }

        /// <summary>
        /// Removes temp files from earlier clipboard shares. Their uploads are long finished by then.
        /// </summary>
        public static void CleanUpTempFiles(TimeSpan olderThan)
        {
            try
            {
                if (!Directory.Exists(AppPaths.ClipboardDirectory))
                    return;
                foreach (var file in Directory.EnumerateFiles(AppPaths.ClipboardDirectory))
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > olderThan)
                        File.Delete(file);
                }
            }
            catch (Exception)
            {
                // Best effort; Windows clears %TEMP% eventually anyway.
            }
        }

        private static string WriteTemp(string fileName, Action<Stream> write)
        {
            Directory.CreateDirectory(AppPaths.ClipboardDirectory);
            var path = Path.Combine(AppPaths.ClipboardDirectory, fileName);
            using (var stream = File.Create(path))
                write(stream);
            return path;
        }
    }
}
