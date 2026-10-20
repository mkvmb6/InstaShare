using InstaShare.Models;

namespace InstaShare.Services
{
    interface IFileManager
    {
        /// <summary>
        /// Uploads a file as a new share.
        /// </summary>
        /// <param name="filePath">The path of the file to upload.</param>
        /// <param name="expiry">How long the share lives. The server deletes it afterwards.</param>
        /// <param name="progress">An optional receiver for throttled progress updates. The first one carries the share's token.</param>
        /// <param name="cancellationToken">Cancels the upload.</param>
        /// <returns>A task that represents the asynchronous operation, containing the ID and shared link of the uploaded file.</returns>
        Task<(string fileId, string sharedLink)> UploadFile(string filePath, TimeSpan expiry, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads the contents of a local folder as a new share, preserving the folder structure.
        /// </summary>
        /// <remarks>A file that fails to upload does not stop the others; it is marked as failed in the index
        /// and returned in <c>failedFiles</c>.</remarks>
        /// <param name="localRootPath">The full path to the local root folder to upload. Must be a valid, accessible directory.</param>
        /// <param name="expiry">How long the share lives. The server deletes it afterwards.</param>
        /// <param name="progress">An optional receiver for throttled progress updates. The first one carries the share's token.</param>
        /// <param name="cancellationToken">Cancels the upload.</param>
        /// <returns>A task that represents the asynchronous operation, containing the ID and shared link of the
        /// root folder and the paths of any files that failed to upload.</returns>
        Task<(string folderId, string sharedLink, IReadOnlyList<string> failedFiles)> UploadFolderWithStructure(string localRootPath, TimeSpan expiry, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads several files and folders as one share, so they get a single link.
        /// </summary>
        /// <remarks>Files sit at the top of the share; each folder keeps its name and structure inside it.
        /// Works like <see cref="UploadFolderWithStructure"/> otherwise, including its <c>failedFiles</c>.</remarks>
        /// <param name="paths">Files and folders, usually what was selected in Explorer.</param>
        /// <param name="shareName">What the share is called in the viewer, e.g. the folder the items were in.</param>
        Task<(string folderId, string sharedLink, IReadOnlyList<string> failedFiles)> UploadItems(IReadOnlyList<string> paths, string shareName, TimeSpan expiry, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads some files of an existing folder share again, typically the ones that failed.
        /// </summary>
        /// <param name="folderId">The folder ID returned by <see cref="UploadFolderWithStructure"/> or <see cref="UploadItems"/>.</param>
        /// <param name="token">The share's token, from the upload's progress reports.</param>
        /// <param name="localPaths">For each file to retry: its path inside the share (forward slashes) and where it is on this PC.</param>
        /// <returns>The paths inside the share that failed again.</returns>
        Task<IReadOnlyList<string>> RetryFolderFiles(string folderId, string token, IReadOnlyDictionary<string, string> localPaths, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a share and everything in it, including its index.
        /// </summary>
        /// <param name="remoteId">The file or folder ID returned by an upload.</param>
        /// <param name="token">The share's token, from the upload's progress reports.</param>
        Task DeleteShare(string remoteId, string token, CancellationToken cancellationToken = default);
    }
}
