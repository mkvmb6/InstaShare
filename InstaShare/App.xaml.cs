using System.Windows;

namespace InstaShare
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            string? pathToUpload = null;
            if (e.Args.Length > 0)
                pathToUpload = e.Args[0];

            var mainWindow = new MainWindow(pathToUpload);
            mainWindow.Show();
        }
    }

}
