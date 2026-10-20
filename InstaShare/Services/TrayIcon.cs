using System.Windows;
using Forms = System.Windows.Forms;

namespace InstaShare.Services
{
    /// <summary>
    /// The notification-area icon that keeps InstaShare reachable while its window is closed.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;

        public TrayIcon()
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/InstaShare.ico")).Stream;
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open InstaShare", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add($"Share clipboard ({GlobalHotkey.DisplayText})", null, (_, _) => ShareClipboardRequested?.Invoke(this, EventArgs.Empty));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

            _icon = new Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(stream),
                Text = Constants.AppName,
                ContextMenuStrip = menu,
                Visible = true,
            };
            _icon.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                    OpenRequested?.Invoke(this, EventArgs.Empty);
            };
        }

        public event EventHandler? OpenRequested;
        public event EventHandler? ShareClipboardRequested;
        public event EventHandler? ExitRequested;

        public string ToolTip
        {
            set
            {
                // NotifyIcon rejects text over 127 characters.
                _icon.Text = value.Length > 127 ? value[..127] : value;
            }
        }

        public void Dispose()
        {
            // Hide first, or the icon lingers in the tray until the mouse passes over it.
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
