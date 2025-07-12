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
        private const string _bucketName = "instashare";
        public static IAmazonS3 CreateR2Client()
        {
            var r2Settings = GetR2Settings();
            var config = new AmazonS3Config
            {
                ForcePathStyle = true,
                RegionEndpoint = RegionEndpoint.USEast1 // Dummy value required
            };
            config.ServiceURL = $"https://{r2Settings.AccountId}.r2.cloudflarestorage.com";

            var credentials = new BasicAWSCredentials(r2Settings.AccessKey, r2Settings.SecretKey);
            return new AmazonS3Client(credentials, config);
        }

        public static string GetPreSignedUrl(this IAmazonS3 s3Client, string fileName)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = fileName,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(TimeSpan.FromHours(10)),
                Protocol = Protocol.HTTPS,
                Headers = { ["x-amz-acl"] = "public-read" } // optional
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