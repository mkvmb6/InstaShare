using InstaShare.FileManagers.Impl;
using InstaShare.Services;
using InstaShare.ViewModels;
using InstaShare.Views;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Windows;

namespace InstaShare
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private SingleInstance? _instance;
        private TrayIcon? _tray;
        private GlobalHotkey? _hotkey;
        private MainWindow? _window;
        private MainViewModel? _viewModel;

        internal static void ApplyTheme(AppTheme theme) =>
            Current.ThemeMode = theme switch
            {
                AppTheme.Light => ThemeMode.Light,
                AppTheme.Dark => ThemeMode.Dark,
                _ => ThemeMode.System,
            };

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            if (e.Args.Contains(Installer.UninstallArgument))
            {
                await RunUninstall();
                return;
            }
            if (Installer.ShouldOfferInstall(e.Args))
            {
                // Downloaded exe: offer to install instead of running from Downloads.
                var installer = new InstallerWindow([.. e.Args.Where(a => a != Installer.InstallArgument)]);
                installer.Closed += (_, _) => Shutdown();
                installer.Show();
                return;
            }

            var toastLaunch = ToastNotificationManagerCompat.WasCurrentProcessToastActivated();
            _instance = SingleInstance.TryAcquire();
            if (_instance == null)
            {
                // A toast click while running is routed to that process by Windows; anything else is forwarded.
                if (toastLaunch || SingleInstance.TrySendToRunningInstance(e.Args))
                {
                    Shutdown();
                    return;
                }
                // The other instance didn't answer (hung or exiting); run on our own rather than doing nothing.
            }

            var settings = AppSettings.Load(AppPaths.SettingsFile);
            ApplyTheme(settings.Theme);

            var fileManager = new CloudFlareR2FileManager();
            var history = new ShareHistory(fileManager, AppPaths.HistoryFile, AppPaths.LegacyHistoryFile);
            var settingsViewModel = new SettingsViewModel(settings, AppPaths.SettingsFile);
            _viewModel = new MainViewModel(fileManager, history, settingsViewModel);

            _window = new MainWindow(_viewModel);
            _window.ExitRequested += (_, _) => ExitApp();

            _tray = new TrayIcon();
            _tray.OpenRequested += (_, _) => _window.ShowAndActivate();
            _tray.ShareClipboardRequested += (_, _) => ShareClipboard();
            _tray.ExitRequested += (_, _) => _window.RequestExit();
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.TrayStatus))
                    _tray.ToolTip = _viewModel.TrayStatus;
            };

            _hotkey = new GlobalHotkey(_window.Handle);
            _hotkey.Pressed += (_, _) => ShareClipboard();
            UpdateHotkey();
            settingsViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SettingsViewModel.GlobalHotkey))
                    UpdateHotkey();
            };

            ToastNotificationManagerCompat.OnActivated += toast => Dispatcher.InvokeAsync(() => OnToastActivated(toast));

            if (_instance != null)
            {
                _instance.ArgumentsReceived += args => Dispatcher.InvokeAsync(() => HandleArguments(args));
                _instance.StartListening();
            }

            // Started with Windows, or by a toast whose handler decides what to show.
            var stayHidden = toastLaunch || e.Args.Contains(WindowsIntegration.BackgroundArgument);
            if (!stayHidden)
                _window.ShowAndActivate();
            HandleArguments(e.Args);
            _ = _viewModel.StartAsync();
        }

        /// <summary>
        /// Paths passed on the command line, by Explorer, Send To or a second launch.
        /// </summary>
        private void HandleArguments(string[] args)
        {
            if (args.Contains(Installer.ExitArgument))
            {
                // The installer is updating or removing this copy.
                _window!.RequestExit();
                return;
            }

            var paths = args.Where(a => !a.StartsWith("--") && !a.StartsWith("-ToastActivated")).ToList();
            if (paths.Count > 0)
            {
                _viewModel!.AddPaths(paths);
                _window!.ShowAndActivate();
            }
            else if (args.Length == 0)
            {
                // Launching InstaShare again just brings it forward.
                _window!.ShowAndActivate();
            }
        }

        private void ShareClipboard()
        {
            var paths = ClipboardService.GetShareablePaths();
            if (paths.Count == 0)
            {
                Toasts.Info("Nothing to share", "Copy a file, folder, screenshot or some text first.");
                return;
            }
            _viewModel!.AddPaths(paths);
            if (!_window!.IsVisible)
                Toasts.Info("Sharing from clipboard", _viewModel.Settings.AutoCopyLink ? "The link will be on your clipboard in a moment." : "Open InstaShare to copy the link.");
        }

        private void UpdateHotkey()
        {
            var settings = _viewModel!.Settings;
            if (!settings.GlobalHotkey)
            {
                _hotkey!.Unregister();
                return;
            }
            settings.HotkeyError = _hotkey!.Register() ? null : $"{GlobalHotkey.DisplayText} is already used by another app.";
        }

        private void OnToastActivated(ToastNotificationActivatedEventArgsCompat toast)
        {
            var args = ToastArguments.Parse(toast.Argument);
            args.TryGetValue(Toasts.ActionKey, out var action);
            args.TryGetValue(Toasts.LinkKey, out var link);
            switch (action)
            {
                case Toasts.CopyLink when link != null:
                    if (!ClipboardService.TrySetText(link))
                        _window!.ShowAndActivate();
                    break;
                case Toasts.OpenLink when link != null:
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true });
                    }
                    catch (Exception)
                    {
                        _window!.ShowAndActivate();
                    }
                    break;
                default:
                    _window!.ShowAndActivate();
                    break;
            }
        }

        private async Task RunUninstall()
        {
            var confirm = MessageBox.Show("Remove InstaShare from this PC?\n\nShares you've already sent keep working until they expire.",
                $"Uninstall {Constants.AppName}", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.OK)
            {
                try
                {
                    await Installer.Uninstall();
                    MessageBox.Show("InstaShare has been removed.", $"Uninstall {Constants.AppName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Couldn't remove InstaShare: {ex.Message}", $"Uninstall {Constants.AppName}", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            Shutdown();
        }

        private void ExitApp()
        {
            _viewModel?.Stop();
            Shutdown();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            _hotkey?.Dispose();
            _tray?.Dispose();
            _instance?.Dispose();
        }
    }
}
