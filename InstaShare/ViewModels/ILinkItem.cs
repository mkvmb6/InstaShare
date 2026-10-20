namespace InstaShare.ViewModels
{
    /// <summary>
    /// Anything with a share link: a transfer in the queue or a share in the history.
    /// </summary>
    internal interface ILinkItem
    {
        string Name { get; }
        string? Link { get; }
    }

    internal sealed record QrViewModel(string Name, string Link, System.Windows.Media.ImageSource Image);
}
