using System.Threading;

namespace Qontract.Core.Interfaces;

/// <summary>
/// Abstraction over the backend that stores document files (issue #34).
/// The local-disk implementation is <c>Qontract.Infrastructure.FileStorage.FileStorage</c>;
/// a cloud backend (Azure Blob / AWS S3, issue #25) can be swapped in without
/// touching the services. Paths are the full values produced by
/// <see cref="GenerateFilePath"/> — implementations resolve them themselves.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Stores <paramref name="content"/> at <paramref name="filePath"/>,
    /// creating the target directory as needed (overwriting any existing
    /// file). Throws <see cref="InvalidOperationException"/> when the content
    /// exceeds the configured maximum file size, and
    /// <see cref="System.IO.IOException"/> on storage failures.
    /// </summary>
    Task SaveAsync(byte[] content, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the file at <paramref name="filePath"/>. Throws
    /// <see cref="System.IO.FileNotFoundException"/> when the file does not exist.
    /// </summary>
    Task<byte[]> ReadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the file at <paramref name="filePath"/>.
    /// Returns <c>true</c> when a file was removed, <c>false</c> when it did not exist.
    /// </summary>
    Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the canonical storage path for a document version
    /// (&lt;root&gt;/contracts/&lt;contractId&gt;/&lt;version&gt;/&lt;fileName&gt;).
    /// Pure: it does not create directories — directory creation is a side
    /// effect of <see cref="SaveAsync"/>.
    /// </summary>
    string GenerateFilePath(int contractId, string fileName, int version);
}
