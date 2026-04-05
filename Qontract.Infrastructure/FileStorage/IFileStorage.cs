namespace Qontract.Infrastructure.FileStorage;

public interface IFileStorage
{
    Task<byte[]> UploadAsync(string fileName, string fileType, long fileSize);
    Task<byte[]> ReadAsync(string filePath);
    Task<bool> DeleteAsync(string filePath);
    string GenerateFilePath(int contractId, string fileName, int version);
}