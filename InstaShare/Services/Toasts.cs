using Microsoft.Toolkit.Uwp.Notifications;

namespace InstaShare.Services
{
    /// <summary>
    /// Windows notifications. Button clicks come back through <see cref="ToastNotificationManagerCompat.OnActivated"/>.
    /// </summary>
    internal static class Toasts
    {
        public const string ActionKey = "action";
        public const string LinkKey = "link";
        public const string OpenApp = "open";
        public const string CopyLink = "copy";
        public const string OpenLink = "browse";
        public const string RestartToUpdate = "update";

        public static void UploadComplete(string name, string link, int failedFiles) =>
            TryShow(new ToastContentBuilder()
                .AddArgument(ActionKey, OpenApp)
                .AddText(failedFiles == 0 ? "Upload complete" : $"Uploaded with {failedFiles} failed file(s)")
                .AddText(name)
                .AddButton(new ToastButton().SetContent("Copy link").AddArgument(ActionKey, CopyLink).AddArgument(LinkKey, link))
                .AddButton(new ToastButton().SetContent("Open").AddArgument(ActionKey, OpenLink).AddArgument(LinkKey, link)));

        public static void UploadFailed(string name, string error) =>
            TryShow(new ToastContentBuilder()
                .AddArgument(ActionKey, OpenApp)
                .AddText("Upload failed")
                .AddText($"{name}: {error}"));

        public static void UpdateReady(Version version) =>
            TryShow(new ToastContentBuilder()
                .AddArgument(ActionKey, OpenApp)
                .AddText("InstaShare update ready")
                .AddText($"Version {version.ToString(3)} installs when you restart InstaShare.")
                .AddButton(new ToastButton().SetContent("Restart now").AddArgument(ActionKey, RestartToUpdate))
                .AddButton(new ToastButton().SetContent("Later").SetDismissActivation()));

        public static void Info(string title, string message) =>
            TryShow(new ToastContentBuilder()
                .AddArgument(ActionKey, OpenApp)
                .AddText(title)
                .AddText(message));

        private static void TryShow(ToastContentBuilder builder)
        {
            try
            {
                builder.Show();
            }
            catch (Exception)
            {
                // Notifications turned off or unavailable; the in-window message still shows.
            }
        }
    }
}
