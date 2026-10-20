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
        /// Uploads some files of an existing folder share again, typically the ones that failed.
        /// </summary>
        /// <param name="folderId">The folder ID returned by <see cref="UploadFolderWithStructure"/>.</param>
        /// <param name="token">The share's token, from the upload's progress reports.</param>
        /// <param name="localRootPath">The local folder that was shared.</param>
        /// <param name="relativePaths">Paths relative to <paramref name="localRootPath"/>, with forward slashes.</param>
        /// <returns>The paths that failed again.</returns>
        Task<IReadOnlyList<string>> RetryFolderFiles(string folderId, string token, string localRootPath, IReadOnlyList<string> relativePaths, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a share and everything in it, including its index.
        /// </summary>
        /// <param name="remoteId">The file or folder ID returned by an upload.</param>
        /// <param name="token">The share's token, from the upload's progress reports.</param>
        Task DeleteShare(string remoteId, string token, CancellationToken cancellationToken = default);
    }
}
