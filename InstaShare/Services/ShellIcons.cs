using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InstaShare.Services
{
    /// <summary>
    /// Thumbnails for images, and the Explorer icon for everything else.
    /// </summary>
    internal static class ShellIcons
    {
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico" };
        private const long MaxThumbnailSourceBytes = 50L * 1024 * 1024;

        public static ImageSource? Get(string path) => TryGetThumbnail(path) ?? TryGetIcon(path);

        private static ImageSource? TryGetThumbnail(string path)
        {
            try
            {
                if (!ImageExtensions.Contains(Path.GetExtension(path)) || new FileInfo(path).Length > MaxThumbnailSourceBytes)
                    return null;

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.DecodePixelWidth = 96;
                image.CacheOption = BitmapCacheOption.OnLoad; // Don't keep the file open
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ImageSource? TryGetIcon(string path)
        {
            var info = new NativeMethods.SHFILEINFO();
            var result = NativeMethods.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }
    }
}
