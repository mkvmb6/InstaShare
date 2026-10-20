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
            // Only the link itself. Apps combine text, link and description into one message,
            // so also setting the link as text or description makes it appear twice.
            data.SetWebLink(new Uri(pending.link));
        }
    }
}
