using CommunityToolkit.Mvvm.Input;

namespace InstaShare.ViewModels
{
    internal enum NoticeSeverity
    {
        Informational,
        Success,
        Warning,
        Error,
    }

    /// <summary>
    /// The message bar at the bottom of the window. Replaces the old modal message boxes.
    /// </summary>
    internal sealed class NoticeViewModel
    {
        public NoticeViewModel(NoticeSeverity severity, string title, string? message = null, string? actionText = null, Action? action = null)
        {
            Severity = severity;
            Title = title;
            Message = message;
            ActionText = actionText;
            if (action != null)
                ActionCommand = new RelayCommand(action);
        }

        public NoticeSeverity Severity { get; }
        public string Title { get; }
        public string? Message { get; }
        public string? ActionText { get; }
        public IRelayCommand? ActionCommand { get; }

        /// <summary>
        /// Good news fades on its own; problems stay until dismissed.
        /// </summary>
        public bool AutoDismiss => Severity is NoticeSeverity.Success or NoticeSeverity.Informational;

        public string Glyph => Severity switch
        {
            NoticeSeverity.Success => "",
            NoticeSeverity.Warning => "",
            NoticeSeverity.Error => "",
            _ => "",
        };
    }
}
