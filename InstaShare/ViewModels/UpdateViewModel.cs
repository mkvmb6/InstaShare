using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InstaShare.Services;
using System.Windows.Threading;

namespace InstaShare.ViewModels
{
    internal enum UpdateState
    {
        Idle,
        Checking,
        Downloading,
        Ready,
        UpToDate,
        Failed,
        Unavailable,
    }

    /// <summary>
    /// Checks GitHub for a new version soon after start and then daily, downloads it in the background,
    /// and installs it when the person restarts (or exits) InstaShare.
    /// </summary>
    internal sealed partial class UpdateViewModel : ObservableObject
    {
        private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        // Downloads wait for uploads to finish, so they don't compete for bandwidth.
        private static readonly TimeSpan BusyRetry = TimeSpan.FromMinutes(10);

        private readonly Updater _updater;
        private readonly Func<bool> _isBusy;
        private readonly Action _exit;
        private readonly Func<bool> _autoUpdate;
        private readonly DispatcherTimer _timer;
        private string? _downloaded;
        private bool _running;

        /// <param name="isBusy">True while uploads are running.</param>
        /// <param name="exit">Closes the app once the new version has been started.</param>
        /// <param name="enabled">False for copies that aren't installed (e.g. running from bin\), which can't update themselves.</param>
        /// <param name="autoUpdate">The "Download updates automatically" setting; manual checks work either way.</param>
        public UpdateViewModel(Updater updater, Func<bool> isBusy, Action exit, bool enabled, Func<bool> autoUpdate)
        {
            _updater = updater;
            _isBusy = isBusy;
            _exit = exit;
            _autoUpdate = autoUpdate;
            CurrentVersion = updater.Current.ToString(3);
            _timer = new DispatcherTimer { Interval = FirstCheckDelay };
            _timer.Tick += async (_, _) => await OnTimer();
            if (!enabled)
            {
                State = UpdateState.Unavailable;
                StatusText = "Updates only work in the installed copy of InstaShare.";
            }
        }

        /// <summary>
        /// Raised on the UI thread once an update has been downloaded and checked.
        /// </summary>
        public event Action<Version>? Ready;

        public string CurrentVersion { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsWorking), nameof(IsChecking), nameof(IsReady))]
        [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
        public partial UpdateState State { get; private set; }

        [ObservableProperty]
        public partial string StatusText { get; private set; } = "";

        [ObservableProperty]
        public partial double Progress { get; private set; }

        public Version? AvailableVersion { get; private set; }

        public bool IsWorking => State is UpdateState.Checking or UpdateState.Downloading;
        public bool IsReady => State == UpdateState.Ready;
        public bool IsChecking => State == UpdateState.Checking;

        public void Start()
        {
            if (State == UpdateState.Unavailable)
                return;
            _updater.CleanUp();
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        [RelayCommand(CanExecute = nameof(CanRunPrimary))]
        private async Task Primary()
        {
            if (State == UpdateState.Ready)
                Restart();
            else
                await CheckAndDownload(manual: true);
        }

        private bool CanRunPrimary() => State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.Unavailable);

        /// <summary>
        /// Starts the downloaded version, which replaces this one once it has exited.
        /// </summary>
        public void Restart()
        {
            if (_downloaded == null)
                return;
            if (_isBusy())
            {
                StatusText = $"Version {AvailableVersion?.ToString(3)} is ready. Restart once your uploads have finished.";
                return;
            }
            try
            {
                Updater.Launch(_downloaded, restart: true);
            }
            catch (Exception ex)
            {
                Fail($"Couldn't start the update: {ex.Message}");
                return;
            }
            // Already handed over; exiting mustn't start it a second time.
            _downloaded = null;
            _exit();
        }

        /// <summary>
        /// Called as the app exits: installs a downloaded update quietly, so the next start runs it.
        /// </summary>
        public void InstallOnExit()
        {
            if (State != UpdateState.Ready || _downloaded == null)
                return;
            try
            {
                Updater.Launch(_downloaded, restart: false);
            }
            catch (Exception)
            {
                // It's offered again next time.
            }
        }

        private async Task OnTimer()
        {
            _timer.Interval = CheckInterval;
            if (!_autoUpdate() || State == UpdateState.Ready)
                return;
            if (_isBusy())
            {
                _timer.Interval = BusyRetry;
                return;
            }
            await CheckAndDownload(manual: false);
        }

        private async Task CheckAndDownload(bool manual)
        {
            if (_running)
                return;
            _running = true;
            try
            {
                State = UpdateState.Checking;
                StatusText = "Checking for updates...";
                var update = await _updater.CheckAsync(CancellationToken.None);
                if (update == null)
                {
                    State = UpdateState.UpToDate;
                    StatusText = $"You have the latest version. Checked {DateTime.Now:t}.";
                    return;
                }

                AvailableVersion = update.Version;
                State = UpdateState.Downloading;
                Progress = 0;
                StatusText = $"Downloading version {update.Version.ToString(3)}...";
                var progress = new Progress<double>(p =>
                {
                    Progress = p;
                    StatusText = $"Downloading version {update.Version.ToString(3)}... {p:P0}";
                });
                _downloaded = await _updater.DownloadAsync(update, progress, CancellationToken.None);

                State = UpdateState.Ready;
                StatusText = $"Version {update.Version.ToString(3)} is ready. It installs when you restart InstaShare.";
                Ready?.Invoke(update.Version);
            }
            catch (Exception ex)
            {
                // Background checks fail quietly (offline, GitHub down); the next one tries again.
                Fail(manual ? $"Couldn't check for updates: {ex.Message}" : "The last update check didn't work. It will try again later.");
            }
            finally
            {
                _running = false;
            }
        }

        private void Fail(string message)
        {
            State = UpdateState.Failed;
            StatusText = message;
        }
    }
}
