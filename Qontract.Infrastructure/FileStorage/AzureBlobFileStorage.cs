using System.IO;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Options;
using Qontract.Core.Interfaces;

namespace Qontract.Infrastructure.FileStorage;

/// <summary>
/// Settings for the Azure Blob Storage backend (issue #25), bound from the
/// "Storage:Azure" configuration section.
/// </summary>
public class AzureBlobStorageOptions
{
    public const string SectionName = "Storage:Azure";

    /// <summary>
    /// Azure storage connection string (account name + key, or a SAS token).
    /// Intentionally not committed — supplied per deployment via environment
    /// variable (Storage__Azure__ConnectionString) or user secrets.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Container all document files are stored in. Created on first write
    /// when it does not exist yet.
    /// </summary>
    public string ContainerName { get; set; } = "documents";
}

/// <summary>
/// Azure Blob Storage implementation of <see cref="IFileStorage"/> (issue
/// #25). Files live at
/// &lt;ContainerName&gt;/contracts/&lt;contractId&gt;/&lt;version&gt;/&lt;fileName&gt;;
/// the container is the root, so the canonical path (and the value persisted
/// in <c>Document.FilePath</c>) is the forward-slash blob key itself. The
/// maximum file size is shared with the local backend via
/// <see cref="FileStorageOptions.MaxFileSizeBytes"/>.
/// </summary>
public class AzureBlobFileStorage : IFileStorage
{
    private readonly FileStorageOptions _storageOptions;
    private readonly BlobContainerClient _container;
    private bool _containerEnsured;

    public AzureBlobFileStorage(
        IOptions<AzureBlobStorageOptions> azureOptions,
        IOptions<FileStorageOptions> storageOptions)
    {
        _storageOptions = storageOptions.Value;
        var options = azureOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Storage:Azure:ConnectionString is not configured. Set it (environment " +
                "variable Storage__Azure__ConnectionString or user secrets — never commit " +
                "it) to use the Azure Blob Storage backend.");
        }

        if (string.IsNullOrWhiteSpace(options.ContainerName))
        {
            throw new InvalidOperationException(
                "Storage:Azure:ContainerName is not configured.");
        }

        // Client construction is offline; no request is made until an
        // operation runs, so the container can be created in DI without a
        // reachable storage account at that point.
        _container = new BlobServiceClient(options.ConnectionString)
            .GetBlobContainerClient(options.ContainerName);
    }

    public string GenerateFilePath(int contractId, string fileName, int version)
    {
        // Forward slashes only: the blob key is the canonical path and must
        // be identical on Linux and Windows hosts (Path.Combine differs).
        return $"contracts/{contractId}/{version}/{fileName}";
    }

    public async Task SaveAsync(byte[] content, string filePath, CancellationToken cancellationToken = default)
    {
        if (content.Length > _storageOptions.MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"File size exceeds maximum allowed size of {_storageOptions.MaxFileSizeBytes / (1024 * 1024)}MB.");
        }

        await EnsureContainerAsync(cancellationToken);
        using var stream = new MemoryStream(content);
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/octet-stream" }
        };
        await _container.GetBlockBlobClient(filePath)
            .UploadAsync(stream, uploadOptions, cancellationToken);
    }

    public async Task<byte[]> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var download = await _container.GetBlockBlobClient(filePath)
                .OpenReadAsync(cancellationToken: cancellationToken);
            using var buffer = new MemoryStream();
            await download.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }
    }

    public async Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // true when a blob was removed, false when it did not exist — the
        // same contract as the disk implementation.
        var result = await _container.GetBlockBlobClient(filePath)
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, null, cancellationToken);
        return result.Value;
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        // Best effort, once per process: creates the container when it does
        // not exist yet. A failed attempt is retried on the next save.
        if (_containerEnsured)
        {
            return;
        }

        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        _containerEnsured = true;
    }
}
