using System.IO;

namespace InstaShare.Services
{
    internal static class AppPaths
    {
        public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Constants.AppName);
        public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
        // Encrypted for the current Windows user; see ApiKeyStore.
        public static string ApiKeyFile => Path.Combine(DataDirectory, "apikey.bin");
        public static string HistoryFile => Path.Combine(DataDirectory, Constants.UploadedFilesJson);
        // Where uploadedFiles.json lived before it moved to LocalAppData.
        public static string LegacyHistoryFile => Path.Combine(AppContext.BaseDirectory, Constants.UploadedFilesJson);
        // Downloaded updates wait here until they're installed.
        public static string UpdateDirectory => Path.Combine(DataDirectory, "updates");
        public static string ClipboardDirectory => Path.Combine(Path.GetTempPath(), Constants.AppName);
        public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, $"{Constants.AppName}.exe");
    }
}
