using CommunityToolkit.Mvvm.ComponentModel;
using InstaShare.Services;

namespace InstaShare.ViewModels
{
    internal partial class SettingsViewModel : ObservableObject
    {
        private readonly AppSettings _settings;
        private readonly string _path;
        private bool _loading = true;

        public SettingsViewModel(AppSettings settings, string path)
        {
            _settings = settings;
            _path = path;
            Theme = settings.Theme;
            DefaultExpiry = ExpiryOption.Closest(settings.DefaultExpiryHours);
            AutoCopyLink = settings.AutoCopyLink;
            ReviewBeforeSharing = settings.ReviewBeforeSharing;
            KeepRunningInTray = settings.KeepRunningInTray;
            GlobalHotkey = settings.GlobalHotkey;
            _loading = false;
        }

        /// <summary>
        /// Raised when a Windows setting couldn't be changed, with the reason.
        /// </summary>
        public event Action<string>? Failed;

        public IReadOnlyList<AppTheme> ThemeOptions { get; } = Enum.GetValues<AppTheme>();
        public IReadOnlyList<ExpiryOption> ExpiryOptions => ExpiryOption.All;
        public string HotkeyText => Services.GlobalHotkey.DisplayText;
        public bool TrayHintShown
        {
            get => _settings.TrayHintShown;
            set
            {
                _settings.TrayHintShown = value;
                Save();
            }
        }

        [ObservableProperty]
        public partial AppTheme Theme { get; set; }

        [ObservableProperty]
        public partial ExpiryOption DefaultExpiry { get; set; }

        [ObservableProperty]
        public partial bool AutoCopyLink { get; set; }

        [ObservableProperty]
        public partial bool ReviewBeforeSharing { get; set; }

        [ObservableProperty]
        public partial bool KeepRunningInTray { get; set; }

        [ObservableProperty]
        public partial bool GlobalHotkey { get; set; }

        /// <summary>
        /// Set by the window when registration fails, e.g. another app owns the shortcut.
        /// </summary>
        [ObservableProperty]
        public partial string? HotkeyError { get; set; }

        partial void OnThemeChanged(AppTheme value)
        {
            App.ApplyTheme(value);
            _settings.Theme = value;
            Save();
        }

        partial void OnDefaultExpiryChanged(ExpiryOption value)
        {
            _settings.DefaultExpiryHours = value.Value.TotalHours;
            Save();
        }

        partial void OnAutoCopyLinkChanged(bool value)
        {
            _settings.AutoCopyLink = value;
            Save();
        }

        partial void OnReviewBeforeSharingChanged(bool value)
        {
            _settings.ReviewBeforeSharing = value;
            Save();
        }

        partial void OnKeepRunningInTrayChanged(bool value)
        {
            _settings.KeepRunningInTray = value;
            Save();
        }

        partial void OnGlobalHotkeyChanged(bool value)
        {
            _settings.GlobalHotkey = value;
            if (!value)
                HotkeyError = null;
            Save();
        }

        public bool StartWithWindows
        {
            get => WindowsIntegration.StartsWithWindows;
            set => ChangeWindowsSetting(() => WindowsIntegration.StartsWithWindows = value, "start-up setting");
        }

        public bool ContextMenu
        {
            get => WindowsIntegration.HasContextMenu;
            set => ChangeWindowsSetting(() => WindowsIntegration.HasContextMenu = value, "Explorer menu");
        }

        public bool SendTo
        {
            get => WindowsIntegration.HasSendTo;
            set => ChangeWindowsSetting(() => WindowsIntegration.HasSendTo = value, "Send to shortcut");
        }

        private void ChangeWindowsSetting(Action change, string what, [System.Runtime.CompilerServices.CallerMemberName] string property = "")
        {
            try
            {
                change();
            }
            catch (Exception ex)
            {
                Failed?.Invoke($"Couldn't change the {what}: {ex.Message}");
            }
            // Re-read from Windows so the toggle shows what actually happened.
            OnPropertyChanged(property);
        }

        private void Save()
        {
            if (_loading)
                return;
            try
            {
                _settings.Save(_path);
            }
            catch (Exception ex)
            {
                Failed?.Invoke($"Couldn't save settings: {ex.Message}");
            }
        }
    }
}
