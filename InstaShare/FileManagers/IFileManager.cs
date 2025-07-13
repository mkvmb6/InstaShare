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
        Task<(string fileId, string sharedLink)> UploadFile(string filePath, string parentFolderId, Action<double, string, string>? reportProgress = null);

        /// <summary>
        /// Retrieves the path of an existing folder or creates a new folder if it does not exist.
        /// </summary>
        /// <remarks>If the specified folder does not exist, it will be created. The method ensures that
        /// the folder is available for use after execution.</remarks>
        /// <param name="folderName">The name of the folder to retrieve or create. Must not be null or empty.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the full path of the folder.</returns>
        Task<string> GetOrCreateFolder(string folderName, string? parentFolderId = null);

        /// <summary>
        /// Shares the specified folder and retrieves a link to access it.
        /// </summary>
        /// <remarks>This method initiates the sharing process for the specified folder and generates a
        /// link that can be used to access the folder. Ensure that the folder exists and the caller has the necessary
        /// permissions to share it.</remarks>
        /// <param name="folderId">The unique identifier of the folder to be shared. This parameter cannot be null or empty.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a string representing the link
        /// to the shared folder.</returns>
        Task<string> ShareFolderAndGetLink(string folderId);

        /// <summary>
        /// Uploads the contents of a local folder to a remote drive, preserving the folder structure.
        /// </summary>
        /// <remarks>This method uploads all files and subdirectories from the specified local folder to
        /// the remote drive. The folder structure is preserved, and the root folder on the remote drive is named
        /// according to <paramref name="driveRootFolderName" />. If <paramref name="statusCallback" /> is provided, it
        /// will be invoked with progress updates, such as the names of files being uploaded.</remarks>
        /// <param name="localRootPath">The full path to the local root folder to upload. Must be a valid, accessible directory.</param>
        /// <param name="driveRootFolderName">The name of the root folder to create on the remote drive. This folder will contain the uploaded files and
        /// subfolders.</param>
        /// <param name="statusCallback">An optional callback that receives status updates during the upload process. The callback parameter is a
        /// string describing the current operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the ID of the created root
        /// folder on the remote drive.</returns>
        Task<(string folderId, string sharedLink)> UploadFolderWithStructure(string localRootPath, string driveRootFolderName, Action<string, string>? statusCallback = null);

        /// <summary>
        /// Deletes a file from the service.
        /// </summary>
        /// <param name="fileId">The ID of the file to delete.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task DeleteFile(string fileId);
    }
}
