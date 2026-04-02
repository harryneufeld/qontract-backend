using Microsoft.Extensions.Configuration;
using Qontract.Application.Interfaces;

namespace Qontract.Infrastructure.FileStorage;

public class FileStorage : IFileStorage
{
    private readonly string _storageRoot;
    private readonly int _maxFileSize = 50 * 1024 * 1024; // 50MB

    public FileStorage(IConfiguration configuration)
    {
        var storagePath = configuration["Storage:Path"] ?? @".\Storage";
        _storageRoot = Path.Combine(storagePath);
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<byte[]> UploadAsync(string fileName, string fileType, long fileSize)
    {
        if (fileSize > _maxFileSize)
            throw new InvalidOperationException($"File size {fileSize} exceeds maximum allowed {_maxFileSize}");

        var directory = Path.Combine(_storageRoot, fileType.Substring(0, fileType.IndexOf('/')));
        Directory.CreateDirectory(directory);

        var fullPath = Path.Combine(directory, fileName);
        var bytes = await File.ReadAllBytesAsync(fullPath);

        return bytes;
    }

    public async Task<byte[]> ReadAsync(string filePath)
    {
        return await File.ReadAllBytesAsync(filePath);
    }

    public Task<bool> DeleteAsync(string filePath)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.FromResult(true);
    }

    public string GenerateFilePath(int contractId, string fileName, int version)
    {
        return Path.Combine(_storageRoot, $"contract_{contractId}_v{version}_{fileName}");
    }
}
