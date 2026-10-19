using Microsoft.Win32;
using System.IO;

namespace InstaShare.FileManagers.Impl
{
    internal static class ContentTypes
    {
        private const string Default = "application/octet-stream";

        // Types the viewer previews, so they don't depend on what's registered on this machine.
        private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = "text/plain; charset=utf-8",
            [".log"] = "text/plain; charset=utf-8",
            [".md"] = "text/markdown; charset=utf-8",
            [".csv"] = "text/csv; charset=utf-8",
            [".json"] = "application/json",
            [".xml"] = "application/xml",
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".bmp"] = "image/bmp",
            [".svg"] = "image/svg+xml",
            [".webp"] = "image/webp",
            [".ico"] = "image/x-icon",
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",
            [".mkv"] = "video/x-matroska",
            [".mov"] = "video/quicktime",
            [".avi"] = "video/x-msvideo",
            [".mp3"] = "audio/mpeg",
            [".wav"] = "audio/wav",
            [".zip"] = "application/zip",
        };

        public static string FromFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
                return Default;
            if (Known.TryGetValue(extension, out var contentType))
                return contentType;

            // Fall back to the type Windows has registered for the extension.
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey(extension);
                return key?.GetValue("Content Type") as string ?? Default;
            }
            catch (Exception)
            {
                return Default;
            }
        }

        /// <summary>
        /// Makes browsers save the file with its original name instead of opening it.
        /// </summary>
        public static string AttachmentDisposition(string fileName)
        {
            var asciiFallback = new string(fileName.Select(c => c is >= ' ' and <= '~' and not '"' and not '\\' ? c : '_').ToArray());
            return $"attachment; filename=\"{asciiFallback}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
        }
    }
}
