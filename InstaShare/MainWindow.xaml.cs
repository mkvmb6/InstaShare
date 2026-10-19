using InstaShare.FileManagers.Impl;
using InstaShare.Models;
using InstaShare.Schedulers;
using InstaShare.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace InstaShare
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IFileManager fileManager;
        private readonly FileDeletionScheduler fileDeletionScheduler;
        private readonly string? startupPath;
        private CancellationTokenSource? uploadCts;
        private bool isBusy;

        public MainWindow(string? pathToUpload = null)
        {
            InitializeComponent();
            fileManager = new CloudFlareR2FileManager();
            fileDeletionScheduler = new FileDeletionScheduler(fileManager);
            startupPath = pathToUpload;
            Loaded += Window_Loaded;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(startupPath))
                return;

            if (File.Exists(startupPath))
                await UploadFile(startupPath);
            else if (Directory.Exists(startupPath))
                await UploadFolder(startupPath);
            else
                ShowError($"Path not found: {startupPath}");
        }

        private async void SelectFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                var filePath = dlg.FileName;
                await UploadFile(filePath);
            }
        }

        private Task UploadFile(string filePath) =>
            RunUpload(async (progress, ct) =>
            {
                var (fileId, link) = await fileManager.UploadFile(filePath, progress, ct);
                return (fileId, link, "Uploaded!");
            }, filePath);

        private async void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog()
            {
                Title = "Select Folder to Upload",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };


            if (dlg.ShowDialog() == true)
            {
                var selectedFolderPath = dlg.FolderName;
                await UploadFolder(selectedFolderPath);
            }

        }

        private Task UploadFolder(string selectedFolderPath) =>
            RunUpload(async (progress, ct) =>
            {
                var (folderId, link, failedFiles) = await fileManager.UploadFolderWithStructure(selectedFolderPath, progress, ct);
                var message = failedFiles.Count == 0
                    ? "✅ Folder uploaded!"
                    : $"⚠ Folder uploaded, but {failedFiles.Count} file(s) failed: {string.Join(", ", failedFiles.Take(3))}{(failedFiles.Count > 3 ? ", ..." : "")}";
                return (folderId, link, message);
            }, selectedFolderPath);

        /// <summary>
        /// Runs one upload with the busy state, progress, cancellation and error reporting handled in one place.
        /// </summary>
        private async Task RunUpload(Func<IProgress<UploadProgress>, CancellationToken, Task<(string remoteId, string link, string doneMessage)>> upload, string localPath)
        {
            if (isBusy)
                return;

            uploadCts = new CancellationTokenSource();
            UploadProgress? lastProgress = null;
            // Created on the UI thread, so reports are posted here without blocking the upload.
            var progress = new Progress<UploadProgress>(p =>
            {
                if (uploadCts == null)
                    return; // Late report after the upload finished
                lastProgress = p;
                SelectedPathText.Text = p.Speed is null ? p.Status : $"{p.Status} - {p.Speed}";
                UploadProgressBar.Value = p.Percent;
                ShowLink(p.SharedLink);
            });

            SetBusy(true);
            SelectedPathText.Text = "Generating link...";
            UploadProgressBar.Value = 0;
            UploadProgressBar.Visibility = Visibility.Visible;
            try
            {
                var (remoteId, link, doneMessage) = await upload(progress, uploadCts.Token);
                SelectedPathText.Text = doneMessage;
                UploadProgressBar.Value = 100;
                ShowLink(link);

                // Store metadata for deletion later
                TrySaveRecord(remoteId, localPath, link);
            }
            catch (OperationCanceledException) when (uploadCts.IsCancellationRequested)
            {
                SelectedPathText.Text = "Upload cancelled.";
                HideLink();
                // Anything already sent still expires with the other old shares.
                if (lastProgress != null)
                    TrySaveRecord(lastProgress.RemoteId, localPath, lastProgress.SharedLink);
            }
            catch (Exception ex)
            {
                SelectedPathText.Text = "Upload failed.";
                HideLink();
                ShowError($"Upload failed: {ex.Message}");
                if (lastProgress != null)
                    TrySaveRecord(lastProgress.RemoteId, localPath, lastProgress.SharedLink);
            }
            finally
            {
                uploadCts.Dispose();
                uploadCts = null;
                UploadProgressBar.Visibility = Visibility.Collapsed;
                SetBusy(false);
            }
        }

        private void TrySaveRecord(string remoteId, string localPath, string link)
        {
            try
            {
                fileDeletionScheduler.SaveFileRecord(remoteId, localPath, link);
            }
            catch (Exception ex)
            {
                ShowError($"The upload worked, but it couldn't be saved for later cleanup: {ex.Message}");
            }
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            SelectFileButton.IsEnabled = !busy;
            SelectFolderButton.IsEnabled = !busy;
            DeleteExpiredButton.IsEnabled = !busy;
            CancelButton.IsEnabled = true;
            CancelButton.Content = "Cancel Upload";
            CancelButton.Visibility = busy && uploadCts != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowLink(string link)
        {
            LinkTextBox.Text = link;
            LinkTextBox.Visibility = Visibility.Visible;
            CopyLinkButton.Visibility = Visibility.Visible;
        }

        private void HideLink()
        {
            LinkTextBox.Visibility = Visibility.Collapsed;
            CopyLinkButton.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message) =>
            MessageBox.Show(this, message, Constants.AppName, MessageBoxButton.OK, MessageBoxImage.Error);

        private void CancelUpload_Click(object sender, RoutedEventArgs e)
        {
            uploadCts?.Cancel();
            CancelButton.IsEnabled = false;
            CancelButton.Content = "Cancelling...";
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (uploadCts == null)
                return;

            var result = MessageBox.Show(this, "An upload is still running. Cancel it and close?", Constants.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                uploadCts.Cancel();
            else
                e.Cancel = true;
        }

        private void CopyLinkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(LinkTextBox.Text);
            }
            catch (Exception ex)
            {
                // Another app can hold the clipboard open.
                ShowError($"Couldn't copy the link: {ex.Message}");
            }
        }


        private async void DeleteExpired_Click(object sender, RoutedEventArgs e)
        {
            if (isBusy)
                return;

            SetBusy(true);
            try
            {
                await fileDeletionScheduler.DeleteExpiredFiles();
                MessageBox.Show(this, "Expired files deleted successfully.", Constants.AppName);
            }
            catch (Exception ex)
            {
                ShowError("Error deleting expired files: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }
    }
}
