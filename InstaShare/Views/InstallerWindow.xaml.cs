using InstaShare.Services;
using System.ComponentModel;
using System.Windows;

namespace InstaShare.Views
{
    /// <summary>
    /// Shown when the exe runs from anywhere other than its install folder.
    /// </summary>
    public partial class InstallerWindow : Window
    {
        private readonly string[] _forwardArgs;
        private bool _busy;

        /// <param name="forwardArgs">Files this launch was given; the installed copy opens them once it starts.</param>
        public InstallerWindow(string[] forwardArgs)
        {
            InitializeComponent();
            _forwardArgs = forwardArgs;

            var options = Installer.CurrentOptions();
            ContextMenuBox.IsChecked = options.ContextMenu;
            SendToBox.IsChecked = options.SendTo;
            StartupBox.IsChecked = options.StartWithWindows;
            DesktopBox.IsChecked = options.DesktopShortcut;

            var current = Installer.CurrentVersion;
            if (Installer.InstalledVersion is { } installed)
            {
                var comparison = current.CompareTo(installed);
                Title = Heading.Text = comparison == 0 ? "Reinstall InstaShare" : "Update InstaShare";
                InstallButton.Content = comparison == 0 ? "Reinstall" : "Update";
                Subheading.Text = comparison switch
                {
                    > 0 => $"Version {installed.ToString(3)} is installed. This updates it to {current.ToString(3)}.",
                    0 => $"Version {current.ToString(3)} is already installed.",
                    _ => $"A newer version ({installed.ToString(3)}) is installed. This replaces it with {current.ToString(3)}.",
                };
            }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true);
            var status = new Progress<string>(text => ShowStatus(text, isError: false));
            try
            {
                await Installer.Install(new Installer.Options(
                    ContextMenu: ContextMenuBox.IsChecked == true,
                    SendTo: SendToBox.IsChecked == true,
                    StartWithWindows: StartupBox.IsChecked == true,
                    DesktopShortcut: DesktopBox.IsChecked == true), status);
            }
            catch (Exception ex)
            {
                SetBusy(false);
                ShowStatus(ex.Message, isError: true);
                return;
            }

            // Installed. Clear the busy flag first, or OnClosing would cancel the Close() below.
            SetBusy(false);
            try
            {
                Installer.LaunchInstalled(_forwardArgs);
            }
            catch (Exception ex)
            {
                // The install itself worked; say so rather than offering to install again.
                ShowStatus($"InstaShare is installed, but couldn't be started: {ex.Message} Open it from the Start menu.", isError: true);
                InstallButton.Visibility = Visibility.Collapsed;
                CancelButton.Content = "Close";
                return;
            }
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            // Stopping halfway through would leave a half-installed copy.
            e.Cancel = _busy;
            base.OnClosing(e);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Choices.IsEnabled = InstallButton.IsEnabled = CancelButton.IsEnabled = !busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Visibility = Visibility.Visible;
            if (isError)
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorCriticalBrush");
            else
                StatusText.ClearValue(ForegroundProperty);
        }
    }
}
