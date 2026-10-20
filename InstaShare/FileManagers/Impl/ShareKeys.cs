namespace InstaShare.FileManagers.Impl
{
    internal static class ShareKeys
    {
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
