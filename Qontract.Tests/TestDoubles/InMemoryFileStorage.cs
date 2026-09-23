using System.Collections.Concurrent;
using System.IO;
using Qontract.Core.Interfaces;

namespace Qontract.Tests.TestDoubles;

/// <summary>
/// In-memory <see cref="IFileStorage"/> for unit tests (issue #34).
/// Mirrors the disk implementation's contract: enforces the maximum file
/// size, throws <see cref="FileNotFoundException"/> for missing files, and
/// keeps files keyed by their full path.
/// </summary>
public class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly int _maxFileSizeBytes;

    public InMemoryFileStorage(int maxFileSizeBytes = 50 * 1024 * 1024)
    {
        _maxFileSizeBytes = maxFileSizeBytes;
    }

    /// <summary>Test hook: snapshot of the currently stored files.</summary>
    public IReadOnlyDictionary<string, byte[]> StoredFiles =>
        _files.ToDictionary(kv => kv.Key, kv => kv.Value);

    public Task SaveAsync(byte[] content, string filePath, CancellationToken cancellationToken = default)
    {
        if (content.Length > _maxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"File size exceeds maximum allowed size of {_maxFileSizeBytes / (1024 * 1024)}MB.");
        }

        _files[filePath] = content;
        return Task.CompletedTask;
    }

    public Task<byte[]> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!_files.TryGetValue(filePath, out var content))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        return Task.FromResult(content);
    }

    public Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_files.TryRemove(filePath, out _));
    }

    public string GenerateFilePath(int contractId, string fileName, int version)
    {
        // Same layout as the disk implementation, rooted at ":memory:".
        return $":memory:/contracts/{contractId}/{version}/{fileName}";
    }
}
