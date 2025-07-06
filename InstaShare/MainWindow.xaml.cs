using Microsoft.Win32;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace InstaShare
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void SelectFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "Select a File to Upload";
            if (dlg.ShowDialog() == true)
            {
                string selectedFilePath = dlg.FileName;
                SelectedPathText.Text = "Selected File: " + selectedFilePath;
                // TODO: Pass this path to your upload logic
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
    }
}