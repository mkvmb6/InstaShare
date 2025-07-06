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
                SelectedPathText.Text = "Generating link...";

                var folderId = await fileManager.GetOrCreateFolder(Constants.AppName);
                var (fileId, link) = await fileManager.UploadFile(filePath, folderId, (progress, sharedLink) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        SelectedPathText.Text = $"Uploading: {progress:0.0}%";
                        LinkTextBox.Text = sharedLink;
                        LinkTextBox.Visibility = Visibility.Visible;
                        CopyLinkButton.Visibility = Visibility.Visible;


                    });
                });

                SelectedPathText.Text = "Uploaded!";
                LinkTextBox.Text = link;
                LinkTextBox.Visibility = Visibility.Visible;
                CopyLinkButton.Visibility = Visibility.Visible;

                // Store metadata for deletion later
                fileDeletionScheduler.SaveFileRecord(fileId, filePath, link);
            }
        }

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
                SelectedPathText.Text = "Generating link...";
                var (folderId, link) = await fileManager.UploadFolderWithStructure(selectedFolderPath, Constants.AppName, (status, shareLink) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        SelectedPathText.Text = status;
                        LinkTextBox.Text = shareLink;
                        LinkTextBox.Visibility = Visibility.Visible;
                        CopyLinkButton.Visibility = Visibility.Visible;
                    });
                });

                SelectedPathText.Text = $"✅ Folder uploaded!";
                LinkTextBox.Text = link;
                LinkTextBox.Visibility = Visibility.Visible;
                CopyLinkButton.Visibility = Visibility.Visible;
                fileDeletionScheduler.SaveFileRecord(folderId, selectedFolderPath, link);
            }

        }
        private void CopyLinkButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(LinkTextBox.Text);
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