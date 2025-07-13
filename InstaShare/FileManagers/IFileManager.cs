namespace InstaShare.Services
{
    interface IFileManager
    {
        /// <summary>
        /// Uploads a file to the service.
        /// </summary>
        /// <param name="filePath">The path of the file to upload.</param>
        /// <param name="reportProgress">An optional action to report upload progress.</param>
        /// <returns>A task that represents the asynchronous operation, containing the URL of the uploaded file.</returns>
        Task<(string fileId, string sharedLink)> UploadFile(string filePath, Action<double, string, string>? reportProgress = null);

        /// <summary>
        /// Uploads the contents of a local folder to a remote drive, preserving the folder structure.
        /// </summary>
        /// <remarks>This method uploads all files and subdirectories from the specified local folder to
        /// the remote drive. The folder structure is preserved, and the root folder on the remote drive is named
        /// according to <paramref name="driveRootFolderName" />. If <paramref name="statusCallback" /> is provided, it
        /// will be invoked with progress updates, such as the names of files being uploaded.</remarks>
        /// <param name="localRootPath">The full path to the local root folder to upload. Must be a valid, accessible directory.</param>
        /// <param name="statusCallback">An optional callback that receives status updates during the upload process. The callback parameter is a
        /// string describing the current operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the ID of the created root
        /// folder on the remote drive.</returns>
        Task<(string folderId, string sharedLink)> UploadFolderWithStructure(string localRootPath, Action<string, string>? statusCallback = null);

        /// <summary>
        /// Deletes a file from the service.
        /// </summary>
        /// <param name="fileId">The ID of the file to delete.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task DeleteFile(string fileId);
    }
}
