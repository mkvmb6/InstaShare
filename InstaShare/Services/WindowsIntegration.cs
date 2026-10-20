using Microsoft.Win32;
using System.IO;

namespace InstaShare.Services
{
    /// <summary>
    /// Per-user Windows hooks: start-up entry, Explorer context menu and Send To shortcut.
    /// Everything is under HKCU or the user profile, so no admin rights are needed and each one is reversible.
    /// </summary>
    internal static class WindowsIntegration
    {
        public const string BackgroundArgument = "--background";

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static readonly string[] ContextMenuKeys = [@"Software\Classes\*\shell\InstaShare", @"Software\Classes\Directory\shell\InstaShare"];
        private static string SendToShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), $"{Constants.AppName}.lnk");
        private static string Exe => AppPaths.ExecutablePath;

        public static bool StartsWithWindows
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(Constants.AppName) != null;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value)
                    key.SetValue(Constants.AppName, $"\"{Exe}\" {BackgroundArgument}");
                else
                    key.DeleteValue(Constants.AppName, throwOnMissingValue: false);
            }
        }

        /// <summary>
        /// "Share with InstaShare" on files and folders. On Windows 11 it appears under "Show more options".
        /// </summary>
        public static bool HasContextMenu
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(ContextMenuKeys[0]);
                return key != null;
            }
            set
            {
                foreach (var path in ContextMenuKeys)
                {
                    if (!value)
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
                        continue;
                    }
                    using var key = Registry.CurrentUser.CreateSubKey(path);
                    key.SetValue("", "Share with InstaShare");
                    key.SetValue("Icon", $"\"{Exe}\",0");
                    using var command = key.CreateSubKey("command");
                    // Explorer starts one process per selected item; the single-instance pipe gathers them into one queue.
                    command.SetValue("", $"\"{Exe}\" \"%1\"");
                }
            }
        }

        /// <summary>
        /// "Send to → InstaShare". Sends every selected item in one launch, and is one click closer on Windows 11.
        /// </summary>
        public static bool HasSendTo
        {
            get => File.Exists(SendToShortcut);
            set
            {
                if (!value)
                {
                    File.Delete(SendToShortcut);
                    return;
                }
                var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host isn't available.");
                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    dynamic shortcut = shell.CreateShortcut(SendToShortcut);
                    shortcut.TargetPath = Exe;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(Exe);
                    shortcut.IconLocation = $"{Exe},0";
                    shortcut.Description = "Share with InstaShare";
                    shortcut.Save();
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
            }
        }
    }
}
