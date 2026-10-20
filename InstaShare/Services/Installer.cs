using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace InstaShare.Services
{
    /// <summary>
    /// The exe installs itself: run from anywhere, it copies itself to %LocalAppData%\Programs\InstaShare and
    /// hooks into Windows. Per user, so no admin prompt. Running a newer exe later updates the installed copy.
    /// </summary>
    internal static class Installer
    {
        public const string InstallArgument = "--install";
        public const string UninstallArgument = "--uninstall";
        public const string ExitArgument = "--exit";

        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\InstaShare";
        private const string Publisher = "YourCodeLab";
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromMinutes(1);

        public static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Constants.AppName);
        public static string InstalledExe => Path.Combine(InstallDirectory, $"{Constants.AppName}.exe");
        private static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{Constants.AppName}.lnk");
        private static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{Constants.AppName}.lnk");

        public static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

        public static Version? InstalledVersion =>
            File.Exists(InstalledExe) && Version.TryParse(FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion, out var version) ? version : null;

        public static bool IsInstalledCopy =>
            string.Equals(Path.GetFullPath(AppPaths.ExecutablePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when this launch should show the installer rather than the app.
        /// </summary>
        public static bool ShouldOfferInstall(string[] args)
        {
            if (args.Contains(InstallArgument))
                return true;
#if DEBUG
            // Running from bin\ while developing; pass --install to try the installer.
            return false;
#else
            return !IsInstalledCopy;
#endif
        }

        public sealed record Options(bool ContextMenu, bool SendTo, bool StartWithWindows, bool DesktopShortcut);

        /// <summary>
        /// What's set up now when updating, or the defaults for a fresh install.
        /// </summary>
        public static Options CurrentOptions() => InstalledVersion == null
            ? new Options(ContextMenu: true, SendTo: true, StartWithWindows: false, DesktopShortcut: false)
            : new Options(WindowsIntegration.HasContextMenu, WindowsIntegration.HasSendTo, WindowsIntegration.StartsWithWindows, File.Exists(DesktopShortcut));

        public static async Task Install(Options options, IProgress<string> status)
        {
            status.Report("Closing InstaShare...");
            await CloseRunningCopies();

            status.Report("Copying InstaShare...");
            await Task.Run(() =>
            {
                Directory.CreateDirectory(InstallDirectory);
                // Copy beside the target, then swap, so a failed copy never leaves a broken exe behind.
                var temp = InstalledExe + ".new";
                File.Copy(AppPaths.ExecutablePath, temp, overwrite: true);
                File.Move(temp, InstalledExe, overwrite: true);
            });

            status.Report("Adding InstaShare to Windows...");
            var exe = InstalledExe;
            WindowsIntegration.SetShortcut(StartMenuShortcut, true, exe);
            WindowsIntegration.SetShortcut(DesktopShortcut, options.DesktopShortcut, exe);
            WindowsIntegration.SetShortcut(WindowsIntegration.SendToShortcut, options.SendTo, exe);
            WindowsIntegration.SetContextMenu(options.ContextMenu, exe);
            WindowsIntegration.SetStartWithWindows(options.StartWithWindows, exe);
            WriteUninstallEntry(exe);
        }

        /// <summary>
        /// Starts the installed copy, passing on any files this launch was given.
        /// </summary>
        public static void LaunchInstalled(IEnumerable<string> args)
        {
            var start = new ProcessStartInfo(InstalledExe) { UseShellExecute = false, WorkingDirectory = InstallDirectory };
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            Process.Start(start);
        }

        /// <summary>
        /// Run by Windows from Apps &amp; features. Removes everything the installer added; settings and history stay
        /// in %LocalAppData%\InstaShare in case it's installed again.
        /// </summary>
        public static async Task Uninstall()
        {
            await CloseRunningCopies();

            var exe = InstalledExe;
            WindowsIntegration.SetContextMenu(false, exe);
            WindowsIntegration.SetStartWithWindows(false, exe);
            foreach (var shortcut in new[] { StartMenuShortcut, DesktopShortcut, WindowsIntegration.SendToShortcut })
                WindowsIntegration.SetShortcut(shortcut, false, exe);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
            try
            {
                ToastNotificationManagerCompat.Uninstall();
            }
            catch (Exception)
            {
                // Notifications were never registered.
            }

            if (!IsInstalledCopy)
            {
                File.Delete(exe);
                TryDeleteEmptyDirectory(InstallDirectory);
                return;
            }

            // A running exe can't delete itself; a hidden cmd waits for this process to exit, then deletes the exe
            // and the folder. rmdir without /s only removes the folder if nothing else is in it.
            Process.Start(new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/d /c ping -n 3 127.0.0.1 >nul & del /f /q \"{exe}\" & rmdir \"{InstallDirectory}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }

        /// <summary>
        /// Asks any running InstaShare to quit (it confirms first if uploads are running), and waits for it.
        /// </summary>
        private static async Task CloseRunningCopies()
        {
            var others = OtherRunningCopies();
            if (others.Count == 0)
                return;

            SingleInstance.TrySendToRunningInstance([ExitArgument]);
            using var timeout = new CancellationTokenSource(CloseTimeout);
            try
            {
                await Task.WhenAll(others.Select(p => p.WaitForExitAsync(timeout.Token)));
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("InstaShare is still running. Close it from its icon in the notification area (right-click, Exit) and try again.");
            }
            finally
            {
                foreach (var process in others)
                    process.Dispose();
            }
        }

        private static List<Process> OtherRunningCopies()
        {
            var self = Environment.ProcessId;
            return [.. Process.GetProcessesByName(Constants.AppName).Where(p =>
            {
                if (p.Id != self)
                    return true;
                p.Dispose();
                return false;
            })];
        }

        private static void WriteUninstallEntry(string exe)
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", Constants.AppName);
            key.SetValue("DisplayVersion", CurrentVersion.ToString(3));
            key.SetValue("Publisher", Publisher);
            key.SetValue("DisplayIcon", $"\"{exe}\",0");
            key.SetValue("InstallLocation", InstallDirectory);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("UninstallString", $"\"{exe}\" {UninstallArgument}");
            key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        private static void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                    Directory.Delete(path);
            }
            catch (IOException)
            {
                // Something else is in there; leave it.
            }
        }
    }
}
