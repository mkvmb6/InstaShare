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
        /// Deletes a file from the service.
        /// </summary>
        /// <param name="fileId">The ID of the file to delete.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task DeleteFile(string fileId);
    }
}
