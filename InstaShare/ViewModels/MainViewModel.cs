using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InstaShare.Models;
using InstaShare.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Shell;
using System.Windows.Threading;

namespace InstaShare.ViewModels
{
    internal enum Page
    {
        Share,
        History,
        Settings,
    }

    internal partial class MainViewModel : ObservableObject
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan NoticeLifetime = TimeSpan.FromSeconds(5);

        private readonly IFileManager _fileManager;
        private readonly ShareHistory _history;
        private readonly DispatcherTimer _cleanupTimer;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _noticeTimer;
        private bool _pumping;
        private bool _unseenFailure;

        public MainViewModel(IFileManager fileManager, ShareHistory history, SettingsViewModel settings)
        {
            _fileManager = fileManager;
            _history = history;
            Settings = settings;
            SelectedExpiry = settings.DefaultExpiry;
            Settings.Failed += message => ShowNotice(new NoticeViewModel(NoticeSeverity.Error, message));
            Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.DefaultExpiry))
                    SelectedExpiry = Settings.DefaultExpiry;
            };

            // Changes can come from any thread; the lists are bound to the UI.
            var dispatcher = Dispatcher.CurrentDispatcher;
            _history.Changed += (_, _) => dispatcher.InvokeAsync(ReloadHistory);
            ReloadHistory();

            _cleanupTimer = new DispatcherTimer { Interval = CleanupInterval };
            _cleanupTimer.Tick += async (_, _) => await CleanUpExpired(userAsked: false);
            _clockTimer = new DispatcherTimer { Interval = ClockInterval };
            _noticeTimer = new DispatcherTimer { Interval = NoticeLifetime };
            _noticeTimer.Tick += (_, _) =>
            {
                _noticeTimer.Stop();
                Notice = null;
            };
            _clockTimer.Tick += (_, _) =>
            {
                foreach (var item in History)
                    item.Tick();
            };
        }

        public SettingsViewModel Settings { get; }
        public ObservableCollection<TransferItemViewModel> Transfers { get; } = [];
        public ObservableCollection<HistoryItemViewModel> History { get; } = [];
        public IReadOnlyList<ExpiryOption> ExpiryOptions => ExpiryOption.All;

        /// <summary>
        /// Set by the window. Toasts are only worth showing when the person isn't looking at it.
        /// </summary>
        public bool IsWindowActive { get; set; }

        /// <summary>
        /// Raised for the share sheet, which needs a window handle.
        /// </summary>
        public event Action<ILinkItem>? ShareSheetRequested;

        [ObservableProperty]
        public partial Page SelectedPage { get; set; }

        [ObservableProperty]
        public partial ExpiryOption SelectedExpiry { get; set; }

        [ObservableProperty]
        public partial NoticeViewModel? Notice { get; set; }

        [ObservableProperty]
        public partial QrViewModel? Qr { get; set; }

        [ObservableProperty]
        public partial double TaskbarProgress { get; set; }

        [ObservableProperty]
        public partial TaskbarItemProgressState TaskbarState { get; set; }

        [ObservableProperty]
        public partial string TrayStatus { get; set; } = Constants.AppName;

        [ObservableProperty]
        public partial bool IsCleaningUp { get; set; }

        public bool HasTransfers => Transfers.Count > 0;
        public bool HasPending => Transfers.Any(t => t.IsPending);
        public bool HasFinished => Transfers.Any(t => t.IsFinished);
        public bool HasHistory => History.Count > 0;
        public bool HasActiveTransfers => Transfers.Any(t => t.State is TransferState.Uploading or TransferState.Queued);

        partial void OnSelectedExpiryChanged(ExpiryOption value)
        {
            // Items waiting for review follow the picker until they're shared.
            foreach (var item in Transfers.Where(t => t.IsPending))
                item.Expiry = value;
        }

        public async Task StartAsync()
        {
            _cleanupTimer.Start();
            _clockTimer.Start();
            ClipboardService.CleanUpTempFiles(TimeSpan.FromDays(1));
            await CleanUpExpired(userAsked: false);
        }

        public void Stop()
        {
            _cleanupTimer.Stop();
            _clockTimer.Stop();
            foreach (var item in Transfers)
                item.Cts?.Cancel();
        }

        /// <summary>
        /// Queues files and folders from any source: drop, browse, paste, command line or a second launch.
        /// </summary>
        public void AddPaths(IEnumerable<string> paths)
        {
            var missing = new List<string>();
            var added = 0;
            foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    missing.Add(path);
                    continue;
                }
                // Dropping the same thing twice before it starts is almost always a mistake.
                if (Transfers.Any(t => t.State is TransferState.Pending or TransferState.Queued && string.Equals(t.LocalPath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                TransferItemViewModel item;
                try
                {
                    item = new TransferItemViewModel(path, SelectedExpiry);
                }
                catch (Exception ex)
                {
                    ShowNotice(new NoticeViewModel(NoticeSeverity.Error, $"Couldn't read {Path.GetFileName(path)}", ex.Message));
                    continue;
                }
                item.State = Settings.ReviewBeforeSharing ? TransferState.Pending : TransferState.Queued;
                item.StatusText = item.IsPending ? "Ready to share" : "Waiting";
                item.PropertyChanged += (_, _) => RaiseQueueChanged();
                Transfers.Insert(0, item);
                _ = item.LoadFolderSizeAsync();
                added++;
            }

            if (missing.Count > 0)
                ShowNotice(new NoticeViewModel(NoticeSeverity.Warning, missing.Count == 1 ? "File not found" : $"{missing.Count} items not found", string.Join(Environment.NewLine, missing.Take(3))));
            if (added > 0)
                SelectedPage = Page.Share;

            RaiseQueueChanged();
            PumpQueue();
        }

        [RelayCommand]
        private void Paste()
        {
            var paths = ClipboardService.GetShareablePaths();
            if (paths.Count == 0)
            {
                ShowNotice(new NoticeViewModel(NoticeSeverity.Informational, "Nothing to share", "Copy a file, folder, screenshot or some text first."));
                return;
            }
            AddPaths(paths);
        }

        [RelayCommand]
        private void ShareAllPending()
        {
            foreach (var item in Transfers.Where(t => t.IsPending))
            {
                item.State = TransferState.Queued;
                item.StatusText = "Waiting";
            }
            PumpQueue();
        }

        [RelayCommand]
        private void ShareItem(TransferItemViewModel item)
        {
            if (!item.IsPending)
                return;
            item.State = TransferState.Queued;
            item.StatusText = "Waiting";
            PumpQueue();
        }

        /// <summary>
        /// Ctrl+C: the link of the newest transfer that has one, else the newest share in the history.
        /// </summary>
        [RelayCommand]
        private void CopyLatestLink()
        {
            ILinkItem? latest = Transfers.FirstOrDefault(t => t.HasUsableLink);
            latest ??= History.FirstOrDefault(h => !h.IsExpired && h.Record.Status is ShareStatus.Completed or ShareStatus.CompletedWithErrors);
            if (latest != null)
                CopyLink(latest);
        }

        [RelayCommand]
        private void CloseItem(TransferItemViewModel item)
        {
            if (item.State == TransferState.Uploading)
            {
                item.IsCancelling = true;
                item.StatusText = "Cancelling...";
                item.Cts?.Cancel();
                UpdateTaskbar();
                return;
            }
            Transfers.Remove(item);
            RaiseQueueChanged();
        }

        [RelayCommand]
        private void Retry(TransferItemViewModel item)
        {
            if (!item.CanRetry)
                return;
            if (item.State == TransferState.CompletedWithErrors && item.RemoteId != null)
                item.RetryFailedOnly = true;
            else
                item.ResetForFullRetry();
            item.State = TransferState.Queued;
            item.StatusText = "Waiting";
            PumpQueue();
        }

        [RelayCommand]
        private void ClearFinished()
        {
            foreach (var item in Transfers.Where(t => t.IsFinished).ToList())
                Transfers.Remove(item);
            RaiseQueueChanged();
        }

        [RelayCommand]
        private void CopyLink(ILinkItem item)
        {
            if (item.Link is not { } link)
                return;
            ShowNotice(ClipboardService.TrySetText(link)
                ? new NoticeViewModel(NoticeSeverity.Success, "Link copied")
                : new NoticeViewModel(NoticeSeverity.Error, "Couldn't copy the link", "Another app is using the clipboard. Try again."));
        }

        [RelayCommand]
        private void OpenLink(ILinkItem item)
        {
            if (item.Link is not { } link)
                return;
            try
            {
                Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowNotice(new NoticeViewModel(NoticeSeverity.Error, "Couldn't open the browser", ex.Message));
            }
        }

        [RelayCommand]
        private void ShowQr(ILinkItem item)
        {
            if (item.Link is { } link)
                Qr = new QrViewModel(item.Name, link, QrCodes.Create(link));
        }

        [RelayCommand]
        private void CloseQr() => Qr = null;

        [RelayCommand]
        private void ShareSheet(ILinkItem item)
        {
            if (item.Link != null)
                ShareSheetRequested?.Invoke(item);
        }

        [RelayCommand]
        private void DismissNotice()
        {
            _noticeTimer.Stop();
            Notice = null;
        }

        [RelayCommand]
        private void AskDelete(HistoryItemViewModel item)
        {
            foreach (var other in History)
                other.IsConfirmingDelete = other == item;
        }

        [RelayCommand]
        private void CancelDelete(HistoryItemViewModel item) => item.IsConfirmingDelete = false;

        [RelayCommand]
        private async Task DeleteShare(HistoryItemViewModel item)
        {
            item.IsConfirmingDelete = false;
            if (item.IsActive)
            {
                ShowNotice(new NoticeViewModel(NoticeSeverity.Warning, "Still uploading", "Cancel the upload first, then delete it."));
                return;
            }
            item.IsDeleting = true;
            try
            {
                await _history.Delete(item.FileId);
                ShowNotice(new NoticeViewModel(NoticeSeverity.Success, $"Deleted {item.Name}", "The link no longer works."));
            }
            catch (Exception ex)
            {
                item.IsDeleting = false;
                ShowNotice(new NoticeViewModel(NoticeSeverity.Error, $"Couldn't delete {item.Name}", ex.Message, "Retry", () => _ = DeleteShare(item)));
            }
        }

        [RelayCommand]
        private Task CleanUpNow() => CleanUpExpired(userAsked: true);

        [RelayCommand]
        private void Navigate(Page page) => SelectedPage = page;

        /// <summary>
        /// Called when the window comes to the front, so a red taskbar button goes back to normal once seen.
        /// </summary>
        public void AcknowledgeFailures()
        {
            if (!_unseenFailure)
                return;
            _unseenFailure = false;
            UpdateTaskbar();
        }

        private async Task CleanUpExpired(bool userAsked)
        {
            if (IsCleaningUp)
                return;
            IsCleaningUp = true;
            try
            {
                var (deleted, failed) = await _history.DeleteExpired(DateTime.UtcNow, IsInUse);
                if (failed > 0)
                    ShowNotice(new NoticeViewModel(NoticeSeverity.Warning, $"{failed} expired share(s) couldn't be deleted", "They'll be tried again in a few minutes."));
                else if (userAsked)
                    ShowNotice(new NoticeViewModel(NoticeSeverity.Success, deleted == 0 ? "Nothing has expired" : $"Deleted {deleted} expired share(s)"));
            }
            catch (Exception ex)
            {
                if (userAsked)
                    ShowNotice(new NoticeViewModel(NoticeSeverity.Error, "Cleanup failed", ex.Message));
            }
            finally
            {
                IsCleaningUp = false;
            }
        }

        private bool IsInUse(string remoteId) =>
            Transfers.Any(t => t.RemoteId == remoteId && t.State is TransferState.Uploading or TransferState.Queued);

        /// <summary>
        /// Runs queued items one at a time, oldest first. Each upload is already parallel inside.
        /// </summary>
        private async void PumpQueue()
        {
            if (_pumping)
                return;
            _pumping = true;
            try
            {
                while (Transfers.LastOrDefault(t => t.State == TransferState.Queued) is { } next)
                    await RunTransfer(next);
            }
            finally
            {
                _pumping = false;
                UpdateTaskbar();
            }
        }

        private async Task RunTransfer(TransferItemViewModel item)
        {
            item.Cts = new CancellationTokenSource();
            item.State = TransferState.Uploading;
            item.IsIndeterminate = true;
            item.Percent = 0;
            item.DetailText = null;
            item.StatusText = item.RetryFailedOnly ? $"Retrying {item.FailedFiles.Count} failed file(s)..." : "Generating link...";
            UpdateTaskbar();

            // Created on the UI thread, so reports are posted here without blocking the upload.
            var progress = new Progress<UploadProgress>(p => OnProgress(item, p));
            var ct = item.Cts.Token;
            try
            {
                IReadOnlyList<string> failedFiles;
                if (item.RetryFailedOnly)
                {
                    failedFiles = await _fileManager.RetryFolderFiles(item.RemoteId!, item.LocalPath, item.FailedFiles, progress, ct);
                }
                else if (item.IsFolder)
                {
                    var (folderId, link, failed) = await _fileManager.UploadFolderWithStructure(item.LocalPath, progress, ct);
                    item.RemoteId = folderId;
                    item.Link = link;
                    failedFiles = failed;
                }
                else
                {
                    var (fileId, link) = await _fileManager.UploadFile(item.LocalPath, progress, ct);
                    item.RemoteId = fileId;
                    item.Link = link;
                    failedFiles = [];
                }
                OnFinished(item, failedFiles);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                OnStopped(item, cancelled: true, error: null);
            }
            catch (Exception ex)
            {
                OnStopped(item, cancelled: false, error: ex.Message);
            }
            finally
            {
                item.Cts.Dispose();
                item.Cts = null;
                item.IsCancelling = false;
                item.IsIndeterminate = false;
                item.RetryFailedOnly = false;
                UpdateTaskbar();
                ReloadHistory();
            }
        }

        private void OnProgress(TransferItemViewModel item, UploadProgress p)
        {
            if (item.State != TransferState.Uploading)
                return; // Late report after the upload finished

            if (!item.RetryFailedOnly && item.RemoteId != p.RemoteId)
            {
                // The link exists before any bytes are sent; record it now so it expires even if the app closes mid-upload.
                item.RemoteId = p.RemoteId;
                item.Link = p.SharedLink;
                var now = DateTime.UtcNow;
                TryChangeHistory(() => _history.Add(new UploadRecord
                {
                    FileId = p.RemoteId,
                    FilePath = item.LocalPath,
                    ShareableLink = p.SharedLink,
                    UploadTimeUtc = now,
                    ExpiresUtc = now + item.Expiry.Value,
                    SizeBytes = p.TotalBytes,
                    IsFolder = item.IsFolder,
                    Status = ShareStatus.Uploading,
                }));
            }

            if (p.LinkLive && !item.IsLinkLive)
            {
                item.IsLinkLive = true;
                if (!item.RetryFailedOnly && Settings.AutoCopyLink)
                {
                    ShowNotice(ClipboardService.TrySetText(p.SharedLink)
                        ? new NoticeViewModel(NoticeSeverity.Success, "Link copied", $"{item.Name} is still uploading, but the link already works.")
                        : new NoticeViewModel(NoticeSeverity.Warning, "Couldn't copy the link", "Another app is using the clipboard.", "Copy link", () => CopyLink(item)));
                }
            }

            item.IsIndeterminate = !p.LinkLive;
            if (!p.LinkLive)
            {
                item.StatusText = p.Status;
                return;
            }

            if (!item.RetryFailedOnly)
                item.TotalBytes = p.TotalBytes;
            item.Percent = p.Percent;

            var parts = new List<string> { $"{Format.Bytes(p.BytesSent)} of {Format.Bytes(p.TotalBytes)}" };
            if (p.Speed != null && p.BytesSent > 0)
                parts.Add(p.Speed);
            if (p.Eta is { } eta)
                parts.Add($"{Format.Duration(eta)} left");
            item.StatusText = item.IsCancelling ? "Cancelling..." : string.Join(" · ", parts);
            item.DetailText = item.IsFolder ? p.Status : null;
            UpdateTaskbar();
        }

        private void OnFinished(TransferItemViewModel item, IReadOnlyList<string> failedFiles)
        {
            item.Percent = 100;
            item.IsLinkLive = true;
            item.SetFailedFiles(failedFiles);
            var expires = $"expires in {Format.Expiry(item.Expiry.Value)}";

            if (failedFiles.Count == 0)
            {
                item.State = TransferState.Completed;
                item.StatusText = item.TotalBytes is { } size ? $"Uploaded · {Format.Bytes(size)} · {expires}" : $"Uploaded · {expires}";
                item.DetailText = null;
                ShowNotice(Settings.AutoCopyLink
                    ? new NoticeViewModel(NoticeSeverity.Success, $"{item.Name} uploaded", "The link is on your clipboard.")
                    : new NoticeViewModel(NoticeSeverity.Success, $"{item.Name} uploaded", null, "Copy link", () => CopyLink(item)));
            }
            else
            {
                item.State = TransferState.CompletedWithErrors;
                item.StatusText = $"Uploaded with {failedFiles.Count} failed file(s)";
                item.DetailText = string.Join(", ", failedFiles.Take(3)) + (failedFiles.Count > 3 ? ", ..." : "");
                ShowNotice(new NoticeViewModel(NoticeSeverity.Warning, $"{failedFiles.Count} file(s) in {item.Name} failed", item.DetailText, item.RetryText, () => Retry(item)));
            }

            TryChangeHistory(() => _history.Update(item.RemoteId!, r => r.Status = failedFiles.Count == 0 ? ShareStatus.Completed : ShareStatus.CompletedWithErrors));
            if (!IsWindowActive)
                Toasts.UploadComplete(item.Name, item.Link!, failedFiles.Count);
        }

        private void OnStopped(TransferItemViewModel item, bool cancelled, string? error)
        {
            if (item.RetryFailedOnly)
            {
                // The share itself is fine; only the retry didn't finish. Go back to offering it.
                item.State = TransferState.CompletedWithErrors;
                item.Percent = 100;
                item.StatusText = $"Uploaded with {item.FailedFiles.Count} failed file(s)";
                if (!cancelled)
                    ShowNotice(new NoticeViewModel(NoticeSeverity.Error, "Retry failed", error, item.RetryText, () => Retry(item)));
                return;
            }

            item.State = cancelled ? TransferState.Cancelled : TransferState.Failed;
            item.StatusText = cancelled ? "Cancelled" : "Upload failed";
            item.DetailText = error;
            if (item.RemoteId != null)
                TryChangeHistory(() => _history.Update(item.RemoteId, r => r.Status = cancelled ? ShareStatus.Cancelled : ShareStatus.Failed));

            if (cancelled)
                return;
            _unseenFailure = !IsWindowActive;
            ShowNotice(new NoticeViewModel(NoticeSeverity.Error, $"{item.Name} failed to upload", error, "Retry", () => Retry(item)));
            if (!IsWindowActive)
                Toasts.UploadFailed(item.Name, error ?? "Unknown error");
        }

        /// <summary>
        /// A history write failing shouldn't break the upload; the share would just miss automatic cleanup.
        /// </summary>
        private void TryChangeHistory(Action change)
        {
            try
            {
                change();
            }
            catch (Exception ex)
            {
                ShowNotice(new NoticeViewModel(NoticeSeverity.Warning, "Couldn't update the history", $"This share may not be deleted automatically: {ex.Message}"));
            }
        }

        private void ReloadHistory()
        {
            IReadOnlyList<UploadRecord> records;
            try
            {
                records = _history.GetAll();
            }
            catch (Exception ex)
            {
                ShowNotice(new NoticeViewModel(NoticeSeverity.Error, "Couldn't read the history", ex.Message));
                return;
            }

            var confirming = History.FirstOrDefault(h => h.IsConfirmingDelete)?.FileId;
            History.Clear();
            foreach (var record in records.OrderByDescending(r => r.UploadTimeUtc))
            {
                History.Add(new HistoryItemViewModel(record)
                {
                    IsActive = IsInUse(record.FileId),
                    IsConfirmingDelete = record.FileId == confirming,
                });
            }
            OnPropertyChanged(nameof(HasHistory));
        }

        private void ShowNotice(NoticeViewModel notice)
        {
            Notice = notice;
            _noticeTimer.Stop();
            if (notice.AutoDismiss)
                _noticeTimer.Start();
        }

        private void RaiseQueueChanged()
        {
            OnPropertyChanged(nameof(HasTransfers));
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(HasFinished));
            OnPropertyChanged(nameof(HasActiveTransfers));
        }

        private void UpdateTaskbar()
        {
            var active = Transfers.Where(t => t.State is TransferState.Uploading or TransferState.Queued).ToList();
            if (active.Count == 0)
            {
                TaskbarState = _unseenFailure ? TaskbarItemProgressState.Error : TaskbarItemProgressState.None;
                TaskbarProgress = _unseenFailure ? 1 : 0;
                TrayStatus = Constants.AppName;
                return;
            }

            var uploading = active.Where(t => t.IsUploading).ToList();
            TaskbarProgress = active.Average(t => t.Percent) / 100;
            TaskbarState = uploading.Any(t => t.IsCancelling) ? TaskbarItemProgressState.Paused
                : uploading.Count > 0 && uploading.All(t => t.IsIndeterminate) && TaskbarProgress == 0 ? TaskbarItemProgressState.Indeterminate
                : TaskbarItemProgressState.Normal;
            var current = uploading.FirstOrDefault();
            TrayStatus = current == null
                ? $"{Constants.AppName} · {active.Count} waiting"
                : $"{Constants.AppName} · {current.Name} {current.Percent:0}%" + (active.Count > 1 ? $" (+{active.Count - 1} more)" : "");
        }
    }
}
