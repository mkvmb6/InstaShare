using CommunityToolkit.Mvvm.ComponentModel;
using InstaShare.Services;
using System.IO;
using System.Windows.Media;

namespace InstaShare.ViewModels
{
    internal enum TransferState
    {
        Pending,   // Waiting for the person to press Share
        Queued,
        Uploading,
        Completed,
        CompletedWithErrors,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// One file or folder in the upload queue.
    /// </summary>
    internal partial class TransferItemViewModel : ObservableObject, ILinkItem
    {
        public TransferItemViewModel(string localPath, ExpiryOption expiry)
        {
            LocalPath = localPath;
            IsFolder = Directory.Exists(localPath);
            Name = Path.GetFileName(localPath.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : localPath;
            Expiry = expiry;
            Icon = ShellIcons.Get(localPath);
            StatusText = "Waiting";
            if (!IsFolder)
                TotalBytes = new FileInfo(localPath).Length;
        }

        public string LocalPath { get; }
        public string Name { get; }
        public bool IsFolder { get; }
        public ImageSource? Icon { get; }
        public ExpiryOption Expiry { get; set; }

        /// <summary>
        /// Set from the first progress report; null until then and again after a full retry.
        /// </summary>
        public string? RemoteId { get; set; }

        /// <summary>
        /// Proves we created the share; needed to retry its failed files.
        /// </summary>
        public string? Token { get; set; }

        public IReadOnlyList<string> FailedFiles { get; private set; } = [];

        /// <summary>
        /// The next run uploads only <see cref="FailedFiles"/> into the existing share.
        /// </summary>
        public bool RetryFailedOnly { get; set; }

        public CancellationTokenSource? Cts { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsUploading), nameof(IsFinished), nameof(CanCancel), nameof(CanRetry), nameof(RetryText), nameof(HasUsableLink), nameof(ShowProgress), nameof(CloseToolTip))]
        public partial TransferState State { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SizeText))]
        public partial long? TotalBytes { get; set; }

        [ObservableProperty]
        public partial double Percent { get; set; }

        [ObservableProperty]
        public partial bool IsIndeterminate { get; set; }

        [ObservableProperty]
        public partial string StatusText { get; set; }

        [ObservableProperty]
        public partial string? DetailText { get; set; }

        [ObservableProperty]
        public partial string? Link { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasUsableLink))]
        public partial bool IsLinkLive { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanCancel))]
        public partial bool IsCancelling { get; set; }

        public string SizeText => TotalBytes is { } bytes ? Format.Bytes(bytes) : IsFolder ? "Folder" : "";
        public bool IsPending => State == TransferState.Pending;
        public bool IsUploading => State == TransferState.Uploading;
        public bool ShowProgress => State is TransferState.Uploading or TransferState.Queued;
        public bool IsFinished => State is TransferState.Completed or TransferState.CompletedWithErrors or TransferState.Failed or TransferState.Cancelled;
        public bool CanCancel => State is TransferState.Pending or TransferState.Queued || (State == TransferState.Uploading && !IsCancelling);
        public bool CanRetry => State is TransferState.Failed or TransferState.Cancelled or TransferState.CompletedWithErrors;
        public string RetryText => State == TransferState.CompletedWithErrors ? $"Retry {FailedFiles.Count} failed" : "Retry";
        public string CloseToolTip => State switch
        {
            TransferState.Uploading => "Cancel upload",
            TransferState.Pending or TransferState.Queued => "Remove from queue",
            _ => "Dismiss",
        };

        public bool HasUsableLink => IsLinkLive && Link != null && State is TransferState.Uploading or TransferState.Completed or TransferState.CompletedWithErrors;

        public void SetFailedFiles(IReadOnlyList<string> failedFiles)
        {
            FailedFiles = failedFiles;
            OnPropertyChanged(nameof(RetryText));
        }

        /// <summary>
        /// Forgets the previous attempt so the next run creates a brand new share.
        /// </summary>
        public void ResetForFullRetry()
        {
            RemoteId = null;
            Token = null;
            Link = null;
            IsLinkLive = false;
            RetryFailedOnly = false;
            SetFailedFiles([]);
            Percent = 0;
            DetailText = null;
        }

        /// <summary>
        /// Folder sizes take a walk of the whole tree, so they're counted off the UI thread.
        /// </summary>
        public async Task LoadFolderSizeAsync()
        {
            if (!IsFolder || TotalBytes != null)
                return;
            try
            {
                TotalBytes = await Task.Run(() => new DirectoryInfo(LocalPath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length));
            }
            catch (Exception)
            {
                // Unreadable subfolder; the upload reports the real total once it starts.
            }
        }
    }
}
