using System.IO;

namespace InstaShare.Services
{
    internal static class AtomicFile
    {
        /// <summary>
        /// Writes to a temp file and swaps it in, so a crash mid-write never leaves a truncated file.
        /// </summary>
        public static void WriteAllText(string path, string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
    }
}
