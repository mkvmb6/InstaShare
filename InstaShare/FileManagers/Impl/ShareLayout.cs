using System.IO;

namespace InstaShare.FileManagers.Impl
{
    /// <summary>
    /// How several selected files and folders are laid out inside one share.
    /// </summary>
    internal static class ShareLayout
    {
        /// <summary>
        /// Files go at the top; each folder keeps its name, with its contents below it. Name clashes
        /// (the same file name picked from two places) get " (2)", " (3)" so nothing is overwritten.
        /// </summary>
        /// <returns>Each file on this PC and its path inside the share, with forward slashes.</returns>
        public static IReadOnlyList<(string localPath, string relativePath)> Arrange(IEnumerable<string> paths)
        {
            var result = new List<(string, string)>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in paths)
            {
                var name = Path.GetFileName(path.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(name))
                    name = "Folder"; // A drive root like "D:\"
                if (Directory.Exists(path))
                {
                    var top = Unique(name, taken, isFolder: true);
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        result.Add((file, $"{top}/{Path.GetRelativePath(path, file).Replace('\\', '/')}"));
                }
                else if (File.Exists(path))
                {
                    result.Add((path, Unique(name, taken, isFolder: false)));
                }
            }
            return result;
        }

        /// <summary>
        /// What a group share is called: the folder the items came from, or "Shared files" when they came from several.
        /// </summary>
        public static string GroupName(IReadOnlyList<string> paths)
        {
            var parents = paths.Select(p => Path.GetDirectoryName(p.TrimEnd('\\', '/'))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (parents.Count == 1 && parents[0] is { } parent && Path.GetFileName(parent) is { Length: > 0 } folder)
                return folder;
            return "Shared files";
        }

        private static string Unique(string name, HashSet<string> taken, bool isFolder)
        {
            if (taken.Add(name))
                return name;
            var stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
            var extension = isFolder ? "" : Path.GetExtension(name);
            for (var n = 2; ; n++)
            {
                var candidate = $"{stem} ({n}){extension}";
                if (taken.Add(candidate))
                    return candidate;
            }
        }
    }
}
