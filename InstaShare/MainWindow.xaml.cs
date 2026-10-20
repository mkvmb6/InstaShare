using CommunityToolkit.Mvvm.Input;
using InstaShare.Services;
using InstaShare.ViewModels;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace InstaShare
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly DependencyProperty IsDragOverProperty =
            DependencyProperty.Register(nameof(IsDragOver), typeof(bool), typeof(MainWindow));

        private readonly MainViewModel _viewModel;
        private bool _exiting;

        internal MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            BrowseFilesCommand = new RelayCommand(BrowseFiles);
            BrowseFolderCommand = new RelayCommand(BrowseFolder);
            viewModel.ShareSheetRequested += item =>
            {
                try
                {
                    ShareSheet.Show(this, item.Name, item.Link!);
                }
                catch (Exception)
                {
                    // Share sheet unavailable (older Windows, or no handle yet); copying is the fallback.
                    viewModel.CopyLinkCommand.Execute(item);
                }
            };

            Activated += (_, _) =>
            {
                viewModel.IsWindowActive = true;
                viewModel.AcknowledgeFailures();
            };
            Deactivated += (_, _) => viewModel.IsWindowActive = false;

            DragEnter += OnDragOver;
            DragOver += OnDragOver;
            DragLeave += (_, _) => IsDragOver = false;
            Drop += OnDrop;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <summary>
        /// Raised when the person really wants to quit, as opposed to hiding the window to the tray.
        /// </summary>
        public event EventHandler? ExitRequested;

        public IRelayCommand BrowseFilesCommand { get; }
        public IRelayCommand BrowseFolderCommand { get; }

        public bool IsDragOver
        {
            get => (bool)GetValue(IsDragOverProperty);
            set => SetValue(IsDragOverProperty, value);
        }

        /// <summary>
        /// The window handle, created without showing the window, so the hotkey works when starting hidden.
        /// </summary>
        public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

        public void ShowAndActivate()
        {
            if (!IsVisible)
                Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            // Windows won't let a background process steal focus; briefly going topmost brings the window forward anyway.
            Topmost = true;
            Topmost = false;
            Focus();
        }

        /// <summary>
        /// Exit from the tray menu: confirm if uploads are running, then quit.
        /// </summary>
        public void RequestExit()
        {
            if (_viewModel.HasActiveTransfers)
            {
                ShowAndActivate();
                var result = MessageBox.Show(this, "Uploads are still running. Cancel them and exit?", Constants.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }
            _exiting = true;
            Close();
        }

        private void BrowseFiles_Click(object sender, RoutedEventArgs e) => BrowseFiles();

        private void BrowseFolder_Click(object sender, RoutedEventArgs e) => BrowseFolder();

        private void BrowseFiles()
        {
            var dlg = new OpenFileDialog { Multiselect = true, Title = "Choose files to share" };
            if (dlg.ShowDialog(this) == true)
                _viewModel.AddPaths(dlg.FileNames);
        }

        private void BrowseFolder()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Choose folders to share",
                Multiselect = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dlg.ShowDialog(this) == true)
                _viewModel.AddPaths(dlg.FolderNames);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            var canDrop = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
            IsDragOver = canDrop;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            IsDragOver = false;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                _viewModel.AddPaths(paths);
            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // Innermost first: QR overlay, then a delete prompt, then the message bar.
                if (_viewModel.Qr != null)
                    _viewModel.Qr = null;
                else if (_viewModel.History.FirstOrDefault(h => h.IsConfirmingDelete) is { } confirming)
                    confirming.IsConfirmingDelete = false;
                else if (_viewModel.Notice != null)
                    _viewModel.Notice = null;
                else
                    return;
                e.Handled = true;
                return;
            }

            // Ctrl+V and Ctrl+C keep their normal meaning inside text boxes.
            if (Keyboard.FocusedElement is TextBox)
                return;
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _viewModel.CopyLatestLinkCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void QrOverlay_MouseDown(object sender, MouseButtonEventArgs e) => _viewModel.Qr = null;

        // Clicks on the card itself shouldn't fall through to the overlay and close it.
        private void QrCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (!_exiting && _viewModel.Settings.KeepRunningInTray)
            {
                // Closing hides to the tray; uploads keep going.
                e.Cancel = true;
                Hide();
                if (!_viewModel.Settings.TrayHintShown)
                {
                    _viewModel.Settings.TrayHintShown = true;
                    Toasts.Info("InstaShare is still running", "It's in the notification area. Right-click its icon to exit.");
                }
                return;
            }

            if (!_exiting && _viewModel.HasActiveTransfers)
            {
                var result = MessageBox.Show(this, "Uploads are still running. Cancel them and close?", Constants.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
