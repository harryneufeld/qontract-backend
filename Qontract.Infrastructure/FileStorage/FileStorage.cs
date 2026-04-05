using System.IO;
using Microsoft.Extensions.Configuration;

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
        {
            throw new InvalidOperationException($"File size exceeds maximum allowed size of {_maxFileSize / (1024 * 1024)}MB");
        }

        var filePath = GenerateFilePath(0, fileName, 1);
        await File.WriteAllBytesAsync(filePath, Array.Empty<byte>());
        return await File.ReadAllBytesAsync(filePath);
    }

    public async Task<byte[]> ReadAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        return await File.ReadAllBytesAsync(filePath);
    }

    public async Task<bool> DeleteAsync(string filePath)
    {
        if (System.IO.File.Exists(filePath))
        {
            await System.IO.File.DeleteAsync(filePath);
            return true;
        }
        return false;
    }

    public string GenerateFilePath(int contractId, string fileName, int version)
    {
        var directory = Path.Combine(_storageRoot, $"contracts\\{contractId}\\{version}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}