namespace InstaShare.FileManagers.Impl
{
    internal static class ShareKeys
    {
        /// <summary>
        /// The viewer link for a share path like "{guid}/My Folder". Each segment is percent-encoded so the link
        /// stays one clickable URL when pasted into chat apps, which stop at the first space.
        /// </summary>
        public static string GetShareLink(string sharePath) => $"{Constants.AppBaseUrl}/{EscapePath(sharePath)}";

        /// <summary>
        /// The public CDN URL of an object. Names with '#', '?' or '%' would otherwise be cut short by the browser.
        /// </summary>
        public static string GetCdnUrl(string key) => $"{Constants.CdnBaseUrl}/{EscapePath(key)}";

        private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

        /// <summary>
        /// Encodes links saved before <see cref="GetShareLink"/> existed. Already-encoded links come back unchanged.
        /// </summary>
        public static string NormalizeLink(string link) =>
            Uri.TryCreate(link, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : link;

        /// <summary>
        /// Returns the top-level folder ("{guid}/" or "{name}-{guid}/") that holds every object of a share.
        /// </summary>
        /// <exception cref="ArgumentException">The ID has no folder part, or that folder doesn't end in a GUID.
        /// This keeps a damaged record from turning into a delete of unrelated objects.</exception>
        public static string GetSharePrefix(string remoteId)
        {
            var slash = remoteId.IndexOf('/');
            if (slash <= 0)
                throw new ArgumentException($"Not a share ID: {remoteId}", nameof(remoteId));

            var folder = remoteId[..slash];
            const int GuidLength = 36;
            if (folder.Length < GuidLength || !Guid.TryParse(folder[^GuidLength..], out _))
                throw new ArgumentException($"Not a share ID: {remoteId}", nameof(remoteId));

            return folder + "/";
        }
    }
}
