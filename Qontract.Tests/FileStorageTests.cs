using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Qontract.Infrastructure.FileStorage;
using Qontract.Tests.TestDoubles;
using Xunit;

namespace Qontract.Tests;

/// <summary>
/// Unit tests for the file storage abstraction (issue #34): the disk
/// implementation <see cref="FileStorage"/> (reshaped SaveAsync/ReadAsync/
/// DeleteAsync contract, config-driven max size, no global lock) and the
/// in-memory test double <see cref="InMemoryFileStorage"/>.
/// </summary>
public class FileStorageTests : IDisposable
{
    private readonly string _storageRoot;

    public FileStorageTests()
    {
        // Per-instance storage root next to the test binaries. Deliberately
        // not Path.GetTempPath() (/tmp): writes there are denied on this host.
        _storageRoot = Path.Combine(AppContext.BaseDirectory, "filestorage-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private static FileStorage NewStorage(string root, int maxFileSizeBytes = 50 * 1024 * 1024)
    {
        return new FileStorage(Options.Create(new FileStorageOptions
        {
            Path = root,
            MaxFileSizeBytes = maxFileSizeBytes
        }));
    }

    [Fact]
    public async Task SaveThenRead_RoundTrips_Bytes()
    {
        var storage = NewStorage(_storageRoot);
        var path = storage.GenerateFilePath(7, "doc.txt", 1);
        var content = new byte[] { 1, 2, 3, 4, 5 };

        await storage.SaveAsync(content, path);

        File.Exists(path).Should().BeTrue();
        (await storage.ReadAsync(path)).Should().Equal(content);
    }

    [Fact]
    public void GenerateFilePath_IsPure_AndProducesExpectedLayout()
    {
        var storage = NewStorage(_storageRoot);

        var path = storage.GenerateFilePath(7, "doc.txt", 3);

        path.Should().Be(Path.Combine(_storageRoot, "contracts", "7", "3", "doc.txt"));
        Directory.Exists(Path.GetDirectoryName(path)!)
            .Should().BeFalse("GenerateFilePath must not create directories; that is a SaveAsync side effect");
    }

    [Fact]
    public async Task SaveAsync_CreatesTargetDirectory()
    {
        var storage = NewStorage(_storageRoot);
        var path = storage.GenerateFilePath(11, "nested.txt", 1);
        Directory.Exists(Path.GetDirectoryName(path)!).Should().BeFalse();

        await storage.SaveAsync(new byte[] { 1 }, path);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_OverMaxSize_ThrowsInvalidOperation()
    {
        var storage = NewStorage(_storageRoot, maxFileSizeBytes: 10);

        var act = () => storage.SaveAsync(new byte[11], Path.Combine(_storageRoot, "too-big.bin"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeds maximum allowed size*");
        File.Exists(Path.Combine(_storageRoot, "too-big.bin")).Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ThrowsFileNotFound()
    {
        var storage = NewStorage(_storageRoot);

        var act = () => storage.ReadAsync(Path.Combine(_storageRoot, "nope.bin"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_RemovesExistingFile_AndReportsMissingAsFalse()
    {
        var storage = NewStorage(_storageRoot);
        var path = storage.GenerateFilePath(1, "del.txt", 1);
        await storage.SaveAsync(new byte[] { 9 }, path);

        (await storage.DeleteAsync(path)).Should().BeTrue();
        File.Exists(path).Should().BeFalse();

        (await storage.DeleteAsync(path)).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_ConcurrentWritesToDifferentFiles_AllPersist()
    {
        // Regression for the removed global SemaphoreSlim: unrelated files
        // must be writable in parallel without serialization.
        var storage = NewStorage(_storageRoot);

        var tasks = Enumerable.Range(0, 8).Select(async i =>
        {
            var path = storage.GenerateFilePath(i, $"f{i}.bin", 1);
            var content = new byte[] { (byte)i, (byte)(i + 100) };
            await storage.SaveAsync(content, path);
            (await storage.ReadAsync(path)).Should().Equal(content);
        });

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task InMemoryFileStorage_SaveReadDelete_RoundTrips()
    {
        var storage = new InMemoryFileStorage();
        var path = storage.GenerateFilePath(1, "a.txt", 1);
        var content = new byte[] { 1, 2, 3 };

        await storage.SaveAsync(content, path);
        storage.StoredFiles.Should().ContainKey(path);
        (await storage.ReadAsync(path)).Should().Equal(content);

        (await storage.DeleteAsync(path)).Should().BeTrue();
        storage.StoredFiles.Should().NotContainKey(path);
        (await storage.DeleteAsync(path)).Should().BeFalse();
    }

    [Fact]
    public async Task InMemoryFileStorage_ReadMissingFile_ThrowsFileNotFound()
    {
        var storage = new InMemoryFileStorage();

        var act = () => storage.ReadAsync(":memory:/contracts/1/1/x.txt");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task InMemoryFileStorage_OverMaxSize_ThrowsInvalidOperation()
    {
        var storage = new InMemoryFileStorage(maxFileSizeBytes: 4);

        var act = () => storage.SaveAsync(new byte[5], ":memory:/contracts/1/1/big.bin");

        await act.Should().ThrowAsync<InvalidOperationException>();
        storage.StoredFiles.Should().BeEmpty();
    }
}
