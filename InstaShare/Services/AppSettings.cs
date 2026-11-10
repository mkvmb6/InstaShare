using Newtonsoft.Json;
using System.IO;

namespace InstaShare.Services
{
    /// <summary>
    /// Preferences stored in settings.json. Things Windows already records (start-up entry, context menu,
    /// Send To shortcut) are read from Windows instead, so they can't drift out of sync.
    /// </summary>
    internal class AppSettings
    {
        public AppTheme Theme { get; set; } = AppTheme.System;
        public double DefaultExpiryHours { get; set; } = 48;
        public bool AutoCopyLink { get; set; } = true;
        public bool ReviewBeforeSharing { get; set; }
        public bool KeepRunningInTray { get; set; } = true;
        public bool GlobalHotkey { get; set; }
        public bool TrayHintShown { get; set; }
        public bool AutoUpdate { get; set; } = true;

        public static AppSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            }
            catch (Exception)
            {
                // A damaged settings file shouldn't stop the app from starting.
            }
            return new AppSettings();
        }

        public void Save(string path) => AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    internal enum AppTheme
    {
        System,
        Light,
        Dark,
    }
}
