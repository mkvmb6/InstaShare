using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Newtonsoft.Json;
using System.IO;
using System.Reflection;

namespace InstaShare.FileManagers.Impl
{
    public static class R2ClientFactory
    {
        public static IAmazonS3 CreateR2Client()
        {
            var r2Settings = GetR2Settings();
            var config = new AmazonS3Config
            {
                ForcePathStyle = true,
                RegionEndpoint = RegionEndpoint.USEast1, // Dummy value required
                // R2 rejects the CRC checksums the SDK adds by default
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            };
            config.ServiceURL = $"https://{r2Settings.AccountId}.r2.cloudflarestorage.com";

            var credentials = new BasicAWSCredentials(r2Settings.AccessKey, r2Settings.SecretKey);
            return new AmazonS3Client(credentials, config);
        }

        /// <summary>
        /// Presigns a PUT for a whole object. The request must send the same Content-Type,
        /// Content-Disposition and x-amz-acl headers, since they are part of the signature.
        /// </summary>
        public static string GetPreSignedUrl(this IAmazonS3 s3Client, string key, string contentType, string? contentDisposition = null)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = Constants.R2BucketName,
                Key = key,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(TimeSpan.FromHours(10)),
                Protocol = Protocol.HTTPS,
                ContentType = contentType,
                Headers = { ["x-amz-acl"] = "public-read" }
            };
            if (contentDisposition != null)
                request.Headers.ContentDisposition = contentDisposition;

            return s3Client.GetPreSignedURL(request);
        }

        /// <summary>
        /// Presigns a PUT for one part of a multipart upload.
        /// </summary>
        public static string GetPreSignedPartUrl(this IAmazonS3 s3Client, string key, string uploadId, int partNumber)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = Constants.R2BucketName,
                Key = key,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(TimeSpan.FromHours(10)),
                Protocol = Protocol.HTTPS,
                UploadId = uploadId,
                PartNumber = partNumber,
            };

            return s3Client.GetPreSignedURL(request);
        }

        private static R2Settings GetR2Settings()
        {
            // Read accessKey, secretKey, and accountId from json file
            var executingAssembly = Assembly.GetExecutingAssembly();
            using var stream = executingAssembly.GetManifestResourceStream("InstaShare.r2_credentials.json");
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<R2Settings>(json);
        }

        private record R2Settings(string AccessKey, string SecretKey, string AccountId);
    }
}
