using System.Windows;
using System.Windows.Interop;
using Windows.ApplicationModel.DataTransfer;

namespace InstaShare.Services
{
    /// <summary>
    /// The Windows share sheet (Mail, Teams, Nearby Share, ...) for a share link.
    /// </summary>
    internal static class ShareSheet
    {
        private static IntPtr _hookedWindow;
        private static (string title, string link)? _pending;

        public static void Show(Window owner, string title, string link)
        {
            var hwnd = new WindowInteropHelper(owner).EnsureHandle();
            if (_hookedWindow != hwnd)
            {
                // One handler per window; it reads whatever was asked for last.
                DataTransferManagerInterop.GetForWindow(hwnd).DataRequested += OnDataRequested;
                _hookedWindow = hwnd;
            }
            _pending = (title, link);
            DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
        }

        private static void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            if (_pending is not { } pending)
                return;
            var data = args.Request.Data;
            data.Properties.Title = pending.title;
            data.Properties.Description = pending.link;
            data.SetWebLink(new Uri(pending.link));
            data.SetText(pending.link);
        }
    }
}
