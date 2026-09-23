using System.IO;
using Microsoft.Extensions.Options;
using Qontract.Core.Interfaces;

namespace Qontract.Infrastructure.FileStorage;

/// <summary>
/// Local-disk implementation of <see cref="IFileStorage"/> (issue #34).
/// Files live under &lt;Storage:Path&gt;/contracts/&lt;contractId&gt;/&lt;version&gt;/&lt;fileName&gt;.
///
/// Deliberately lock-free: the old global <c>SemaphoreSlim(1,1)</c> serialized
/// every read/write/delete in the process, even for unrelated files. Plain
/// <c>File</c> I/O is safe for the concurrent use here (each document write
/// goes to its own path); a shared storage backend (issue #25) can impose its
/// own concurrency rules if it needs them.
/// </summary>
public class FileStorage : IFileStorage
{
    private readonly FileStorageOptions _options;
    private readonly string _storageRoot;

    public FileStorage(IOptions<FileStorageOptions> options)
    {
        _options = options.Value;
        // Relative paths resolve against the process working directory
        // (the API project), so storage ends up next to the SQLite db.
        _storageRoot = Path.GetFullPath(_options.Path);
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task SaveAsync(byte[] content, string filePath, CancellationToken cancellationToken = default)
    {
        if (content.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"File size exceeds maximum allowed size of {_options.MaxFileSizeBytes / (1024 * 1024)}MB.");
        }

        // Directory creation is a side effect of saving, not of path generation.
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllBytesAsync(filePath, content, cancellationToken);
    }

    public async Task<byte[]> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        return await File.ReadAllBytesAsync(filePath, cancellationToken);
    }

    public Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public string GenerateFilePath(int contractId, string fileName, int version)
    {
        // Path.Combine (not literal backslashes) so the layout is correct on
        // Linux and Windows alike: <root>/contracts/<contractId>/<version>/<file>.
        // Pure — SaveAsync creates the directory when writing.
        return Path.Combine(
            _storageRoot, "contracts", contractId.ToString(), version.ToString(), fileName);
    }
}
