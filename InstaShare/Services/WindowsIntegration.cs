using Microsoft.Win32;
using System.IO;

namespace InstaShare.Services
{
    /// <summary>
    /// Per-user Windows hooks: start-up entry, Explorer context menu and shortcuts.
    /// Everything is under HKCU or the user profile, so no admin rights are needed and each one is reversible.
    /// </summary>
    /// <remarks>The properties act on the running exe; the installer uses the methods to point them at the installed copy.</remarks>
    internal static class WindowsIntegration
    {
        public const string BackgroundArgument = "--background";

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static readonly string[] ContextMenuKeys = [@"Software\Classes\*\shell\InstaShare", @"Software\Classes\Directory\shell\InstaShare"];
        public static string SendToShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), $"{Constants.AppName}.lnk");
        private static string Exe => AppPaths.ExecutablePath;

        public static bool StartsWithWindows
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(Constants.AppName) != null;
            }
            set => SetStartWithWindows(value, Exe);
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
            set => SetContextMenu(value, Exe);
        }

        /// <summary>
        /// "Send to → InstaShare". Sends every selected item in one launch, and is one click closer on Windows 11.
        /// </summary>
        public static bool HasSendTo
        {
            get => File.Exists(SendToShortcut);
            set => SetShortcut(SendToShortcut, value, Exe);
        }

        public static void SetStartWithWindows(bool enabled, string exe)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(Constants.AppName, $"\"{exe}\" {BackgroundArgument}");
            else
                key.DeleteValue(Constants.AppName, throwOnMissingValue: false);
        }

        public static void SetContextMenu(bool enabled, string exe)
        {
            foreach (var path in ContextMenuKeys)
            {
                if (!enabled)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
                    continue;
                }
                using var key = Registry.CurrentUser.CreateSubKey(path);
                key.SetValue("", "Share with InstaShare");
                key.SetValue("Icon", $"\"{exe}\",0");
                using var command = key.CreateSubKey("command");
                // Explorer starts one process per selected item; the single-instance pipe gathers them into one queue.
                command.SetValue("", $"\"{exe}\" \"%1\"");
            }
            // Tell Explorer, so the menu changes without signing out.
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Creates or removes a shortcut to <paramref name="exe"/>.
        /// </summary>
        public static void SetShortcut(string shortcutPath, bool enabled, string exe)
        {
            if (!enabled)
            {
                File.Delete(shortcutPath);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
            var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host isn't available.");
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
                shortcut.IconLocation = $"{exe},0";
                shortcut.Description = "Share files and folders with a link";
                shortcut.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
