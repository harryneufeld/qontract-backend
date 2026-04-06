using System.IO;
using Microsoft.Extensions.Configuration;
using Qontract.Core.Interfaces;

namespace Qontract.Infrastructure.FileStorage;

public class FileStorage : IFileStorage
{
    private readonly string _storageRoot;
    private readonly int _maxFileSize = 50 * 1024 * 1024; // 50MB
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public FileStorage(IConfiguration configuration)
    {
        var storagePath = configuration["Storage:Path"] ?? @".\Storage";
        _storageRoot = Path.Combine(storagePath);
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<byte[]> UploadAsync(string fileName, string fileType, long fileSize)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (fileSize > _maxFileSize)
            {
                throw new InvalidOperationException($"File size exceeds maximum allowed size of {_maxFileSize / (1024 * 1024)}MB");
            }

            var filePath = GenerateFilePath(0, fileName, 1);
            // Store file data
            var fileData = new byte[0];
            await File.WriteAllBytesAsync(filePath, fileData);
            return await File.ReadAllBytesAsync(filePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<byte[]> ReadAsync(string filePath)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            return await File.ReadAllBytesAsync(filePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> DeleteAsync(string filePath)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
                return true;
            }
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public string GenerateFilePath(int contractId, string fileName, int version)
    {
        var directory = Path.Combine(_storageRoot, $"contracts\\{contractId}\\{version}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}