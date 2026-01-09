using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

namespace GlobalAdmin.Services;

public class StorageCleanupService
{
    private readonly IConfiguration _config;
    private readonly ILogger<StorageCleanupService> _logger;

    public StorageCleanupService(IConfiguration config, ILogger<StorageCleanupService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<int> DeleteWorkspaceStorageAsync(string workspaceId)
    {
        var s3Config = _config.GetSection("S3Storage");
        var accessKey = s3Config["AccessKey"];
        var secretKey = s3Config["SecretKey"];
        var bucket = s3Config["Bucket"];
        var region = s3Config["Region"] ?? "us-east-1";
        var storageRoot = s3Config["StorageRoot"] ?? "";
        var customEndpoint = s3Config["CustomEndpoint"];

        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(bucket))
        {
            _logger.LogWarning("S3 storage not configured, skipping storage cleanup for workspace {WorkspaceId}", workspaceId);
            return 0;
        }

        // Build prefix: {storageRoot}/rev_{workspaceId}/
        var prefix = string.IsNullOrEmpty(storageRoot)
            ? $"rev_{workspaceId}/"
            : $"{storageRoot}/rev_{workspaceId}/";

        _logger.LogInformation("Deleting S3 objects with prefix: {Prefix} from bucket {Bucket}", prefix, bucket);

        var s3ClientConfig = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(region)
        };

        // Support custom endpoint (MinIO, etc.)
        if (!string.IsNullOrEmpty(customEndpoint))
        {
            s3ClientConfig.ServiceURL = customEndpoint;
            s3ClientConfig.ForcePathStyle = true;
        }

        using var s3Client = new AmazonS3Client(accessKey, secretKey, s3ClientConfig);

        var totalDeleted = 0;
        string? continuationToken = null;

        do
        {
            var listRequest = new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken
            };

            var listResponse = await s3Client.ListObjectsV2Async(listRequest);

            if (listResponse.S3Objects.Count > 0)
            {
                var deleteRequest = new DeleteObjectsRequest
                {
                    BucketName = bucket,
                    Objects = listResponse.S3Objects
                        .Select(o => new KeyVersion { Key = o.Key })
                        .ToList()
                };

                var deleteResponse = await s3Client.DeleteObjectsAsync(deleteRequest);
                totalDeleted += deleteResponse.DeletedObjects.Count;

                _logger.LogInformation("Deleted {Count} objects from S3", deleteResponse.DeletedObjects.Count);
            }

            continuationToken = listResponse.IsTruncated ? listResponse.NextContinuationToken : null;

        } while (continuationToken != null);

        _logger.LogInformation("Total S3 objects deleted for workspace {WorkspaceId}: {Count}", workspaceId, totalDeleted);
        return totalDeleted;
    }
}
