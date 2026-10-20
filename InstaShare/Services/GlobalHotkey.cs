using System.Windows.Interop;

namespace InstaShare.Services
{
    /// <summary>
    /// Ctrl+Alt+U anywhere in Windows. Shares what's on the clipboard.
    /// </summary>
    internal sealed class GlobalHotkey : IDisposable
    {
        public const string DisplayText = "Ctrl+Alt+U";
        private const int Id = 0x5153; // Any app-unique value
        private const uint VK_U = 0x55;

        private readonly HwndSource _source;
        private bool _registered;

        public GlobalHotkey(IntPtr windowHandle)
        {
            _source = HwndSource.FromHwnd(windowHandle);
            _source.AddHook(WndProc);
        }

        public event EventHandler? Pressed;

        public bool IsRegistered => _registered;

        /// <returns>False if another app already owns the shortcut.</returns>
        public bool Register()
        {
            if (!_registered)
                _registered = NativeMethods.RegisterHotKey(_source.Handle, Id, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, VK_U);
            return _registered;
        }

        public void Unregister()
        {
            if (_registered)
                NativeMethods.UnregisterHotKey(_source.Handle, Id);
            _registered = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == Id)
            {
                Pressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Unregister();
            _source.RemoveHook(WndProc);
        }
    }
}
