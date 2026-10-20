using CommunityToolkit.Mvvm.ComponentModel;
using InstaShare.Models;
using InstaShare.Services;

namespace InstaShare.ViewModels
{
    internal partial class HistoryItemViewModel(UploadRecord record) : ObservableObject, ILinkItem
    {
        public UploadRecord Record { get; } = record;
        public string Name => Record.DisplayName;
        public string? Link => Record.ShareableLink;
        public string FileId => Record.FileId;

        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (Record.SizeBytes is { } size)
                    parts.Add(Format.Bytes(size));
                parts.Add($"Uploaded {Record.UploadTimeUtc.ToLocalTime():MMM d, HH:mm}");
                if (StatusText is { } status)
                    parts.Add(status);
                return string.Join(" · ", parts);
            }
        }

        private string? StatusText => Record.Status switch
        {
            ShareStatus.CompletedWithErrors => "Some files failed",
            ShareStatus.Failed => "Upload failed",
            ShareStatus.Cancelled => "Cancelled",
            // Still "uploading" in the file but nothing is running it: the app closed mid-upload.
            ShareStatus.Uploading when !IsActive => "Interrupted",
            ShareStatus.Uploading => "Uploading",
            _ => null,
        };

        /// <summary>
        /// True while the upload queue is still working on this share.
        /// </summary>
        public bool IsActive { get; set; }

        public bool IsExpired => Record.EffectiveExpiresUtc <= DateTime.UtcNow;
        public string ExpiresText => Format.ExpiresIn(Record.EffectiveExpiresUtc, DateTime.UtcNow);

        [ObservableProperty]
        public partial bool IsDeleting { get; set; }

        /// <summary>
        /// Delete asks "Delete for everyone?" inline before anything is removed.
        /// </summary>
        [ObservableProperty]
        public partial bool IsConfirmingDelete { get; set; }

        /// <summary>
        /// Re-reads the clock-based text; called on a timer.
        /// </summary>
        public void Tick()
        {
            OnPropertyChanged(nameof(ExpiresText));
            OnPropertyChanged(nameof(IsExpired));
        }
    }
}
