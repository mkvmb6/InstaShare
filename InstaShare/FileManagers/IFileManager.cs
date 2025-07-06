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
        Task<(string fileId, string sharedLink)> UploadFile(string filePath, string parentFolderId, Action<double> reportProgress = null);

        /// <summary>
        /// Gets or creates a folder in the service.
        /// </summary>
        /// <param name="folderName"></param>
        /// <returns></returns>
        Task<string> GetOrCreateFolder(string folderName);


        /// <summary>
        /// Deletes a file from the service.
        /// </summary>
        /// <param name="fileId">The ID of the file to delete.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task DeleteFile(string fileId);
    }
}
