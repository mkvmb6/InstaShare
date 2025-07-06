using InstaShare.Schedulers;
using InstaShare.Services;
using Microsoft.Win32;
using System.Windows;

namespace InstaShare
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IFileManager fileManager;
        private FileDeletionScheduler fileDeletionScheduler;
        public MainWindow()
        {
            InitializeComponent();
            fileManager = new GoogleDriveFileManager();
            fileDeletionScheduler = new FileDeletionScheduler();
        }

        private async void SelectFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            if (dlg.ShowDialog() == true)
            {
                var filePath = dlg.FileName;
                SelectedPathText.Text = "Uploading...";

                var folderId = await fileManager.GetOrCreateFolder(Constants.AppName);
                var (fileId, link) = await fileManager.UploadFile(filePath, folderId, (progress) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Upload progress: {progress:0.0}%");
                    Dispatcher.Invoke(() =>
                    {
                        SelectedPathText.Text = $"Uploading: {progress:0.0}%";
                    });
                });

                SelectedPathText.Text = "Uploaded! Link:\n" + link;

                // Store metadata for deletion later
                fileDeletionScheduler.SaveFileRecord(fileId, filePath, link);
            }
        }

        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            //using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            //{
            //    dlg.Description = "Select a Folder to Upload";
            //    dlg.UseDescriptionForTitle = true;

            //    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            //    {
            //        string selectedFolderPath = dlg.SelectedPath;
            //        SelectedPathText.Text = "Selected Folder: " + selectedFolderPath;
            //        // TODO: Pass this path to your folder upload logic
            //    }
            //}
        }

        private void DeleteExpired_Click(object sender, RoutedEventArgs e)
        {
            fileDeletionScheduler.DeleteExpiredFiles().ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Expired files deleted successfully.");
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Error deleting expired files: " + task.Exception?.Message);
                    });
                }
            });
        }
    }
}