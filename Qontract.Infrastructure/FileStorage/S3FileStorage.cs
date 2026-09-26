using System.IO;
using System.Net;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Qontract.Core.Interfaces;

namespace Qontract.Infrastructure.FileStorage;

/// <summary>
/// Settings for the Amazon S3 backend (issue #25), bound from the
/// "Storage:S3" configuration section. Credentials come from the standard
/// AWS chain (environment variables, shared profile, IAM role) — they are
/// never committed in configuration.
/// </summary>
public class S3StorageOptions
{
    public const string SectionName = "Storage:S3";

    /// <summary>Bucket all document files are stored in (must exist).</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// Optional key prefix all objects are stored under (e.g. "qontract").
    /// Leading/trailing slashes are ignored.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// AWS region (e.g. "eu-central-1"). Optional: when empty the client
    /// uses the default region chain (AWS_REGION / AWS_DEFAULT_REGION /
    /// instance metadata).
    /// </summary>
    public string Region { get; set; } = string.Empty;
}

/// <summary>
/// Amazon S3 implementation of <see cref="IFileStorage"/> (issue #25).
/// Objects live at &lt;Prefix&gt;/contracts/&lt;contractId&gt;/&lt;version&gt;/&lt;fileName&gt;;
/// the canonical path (and the value persisted in <c>Document.FilePath</c>)
/// is the forward-slash object key itself. The maximum file size is shared
/// with the local backend via <see cref="FileStorageOptions.MaxFileSizeBytes"/>.
/// </summary>
public class S3FileStorage : IFileStorage
{
    private readonly FileStorageOptions _storageOptions;
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _prefix;

    public S3FileStorage(
        IOptions<S3StorageOptions> s3Options,
        IOptions<FileStorageOptions> storageOptions)
    {
        _storageOptions = storageOptions.Value;
        var options = s3Options.Value;
        if (string.IsNullOrWhiteSpace(options.Bucket))
        {
            throw new InvalidOperationException(
                "Storage:S3:Bucket is not configured. Set it (environment variable " +
                "Storage__S3__Bucket or user secrets) to use the Amazon S3 backend.");
        }

        _bucket = options.Bucket;
        _prefix = (options.Prefix ?? string.Empty).Trim().Trim('/');

        // Client construction is offline; no request is made until an
        // operation runs. GetBySystemName accepts real and "virtual"
        // regions alike, so a misconfigured region surfaces on the first
        // storage operation, not at startup.
        _client = string.IsNullOrWhiteSpace(options.Region)
            ? new AmazonS3Client()
            : new AmazonS3Client(RegionEndpoint.GetBySystemName(options.Region));
    }

    public string GenerateFilePath(int contractId, string fileName, int version)
    {
        // Forward slashes only: the object key is the canonical path and
        // must be identical on Linux and Windows hosts (Path.Combine
        // differs). The prefix is applied at operation time, not here —
        // Document.FilePath stays portable across prefix changes.
        return $"contracts/{contractId}/{version}/{fileName}";
    }

    public async Task SaveAsync(byte[] content, string filePath, CancellationToken cancellationToken = default)
    {
        if (content.Length > _storageOptions.MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"File size exceeds maximum allowed size of {_storageOptions.MaxFileSizeBytes / (1024 * 1024)}MB.");
        }

        using var stream = new MemoryStream(content);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = ToObjectKey(filePath),
            InputStream = stream,
            ContentType = "application/octet-stream"
        }, cancellationToken);
    }

    public async Task<byte[]> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        GetObjectResponse response;
        try
        {
            response = await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucket,
                Key = ToObjectKey(filePath)
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        using (response)
        using (var buffer = new MemoryStream())
        {
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
    }

    public async Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // Existence check first so the return value keeps the IFileStorage
        // contract (true = removed, false = did not exist).
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, ToObjectKey(filePath), cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await _client.DeleteObjectAsync(_bucket, ToObjectKey(filePath), cancellationToken);
        return true;
    }

    /// <summary>
    /// Resolves a canonical path (from <see cref="GenerateFilePath"/>) to
    /// the full S3 object key by applying the configured prefix. Pure —
    /// used by Save/Read/Delete and unit-testable without a network.
    /// </summary>
    public string ToObjectKey(string filePath) =>
        _prefix.Length == 0 ? filePath : $"{_prefix}/{filePath}";
}
