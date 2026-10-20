using InstaShare.Models;

namespace InstaShare.Services
{
    interface IFileManager
    {
        /// <summary>
        /// Uploads a file to the service.
        /// </summary>
        /// <param name="filePath">The path of the file to upload.</param>
        /// <param name="progress">An optional receiver for throttled progress updates.</param>
        /// <param name="cancellationToken">Cancels the upload.</param>
        /// <returns>A task that represents the asynchronous operation, containing the ID and shared link of the uploaded file.</returns>
        Task<(string fileId, string sharedLink)> UploadFile(string filePath, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads the contents of a local folder to a remote drive, preserving the folder structure.
        /// </summary>
        /// <remarks>A file that fails to upload does not stop the others; it is marked as failed in the index
        /// and returned in <c>failedFiles</c>.</remarks>
        /// <param name="localRootPath">The full path to the local root folder to upload. Must be a valid, accessible directory.</param>
        /// <param name="progress">An optional receiver for throttled progress updates.</param>
        /// <param name="cancellationToken">Cancels the upload.</param>
        /// <returns>A task that represents the asynchronous operation, containing the ID and shared link of the
        /// root folder and the paths of any files that failed to upload.</returns>
        Task<(string folderId, string sharedLink, IReadOnlyList<string> failedFiles)> UploadFolderWithStructure(string localRootPath, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads some files of an existing folder share again, typically the ones that failed.
        /// </summary>
        /// <param name="folderId">The folder ID returned by <see cref="UploadFolderWithStructure"/>.</param>
        /// <param name="localRootPath">The local folder that was shared.</param>
        /// <param name="relativePaths">Paths relative to <paramref name="localRootPath"/>, with forward slashes.</param>
        /// <returns>The paths that failed again.</returns>
        Task<IReadOnlyList<string>> RetryFolderFiles(string folderId, string localRootPath, IReadOnlyList<string> relativePaths, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a share and everything in it, including its index.
        /// </summary>
        /// <param name="remoteId">The file or folder ID returned by an upload.</param>
        Task DeleteShare(string remoteId, CancellationToken cancellationToken = default);
    }
}
