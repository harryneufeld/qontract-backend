using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Qontract.Application.Models;
using Qontract.Application.Services;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Qontract.Tests.TestDoubles;
using Xunit;

namespace Qontract.Tests;

/// <summary>
/// Unit tests proving that DocumentService routes all file I/O through
/// IFileStorage (issue #34) instead of System.IO.File: the storage is the
/// in-memory test double, so anything landing on disk would be a bug.
/// Covers the save path (upload), the read path (download) and the
/// storage-enforced max file size.
/// </summary>
public class DocumentServiceFileStorageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly InMemoryFileStorage _fileStorage = new(maxFileSizeBytes: 1024);
    private readonly DocumentService _service;

    public DocumentServiceFileStorageTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
        _service = new DocumentService(
            _context, _fileStorage,
            new Microsoft.Extensions.Options.OptionsWrapper<DocumentRetentionOptions>(new DocumentRetentionOptions()));

        // Seed the tenant + contract the service resolves on every call.
        _context.Tenants.Add(new Tenant
        {
            Name = "Unit Test Tenant",
            Type = TenantType.Family,
            IsActive = true
        });
        _context.SaveChanges();
        var tenant = _context.Tenants.First();
        _context.Contracts.Add(new Contract
        {
            Title = "Unit test contract",
            Status = ContractStatus.Active,
            IsActive = true,
            TenantId = tenant.Id
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private int ContractId => _context.Contracts.AsNoTracking().First().Id;

    private static DocumentCreateDTO UploadDto(string fileName, byte[] content) => new()
    {
        FileName = fileName,
        FileType = "text/plain",
        FileData = Convert.ToBase64String(content)
    };

    [Fact]
    public async Task CreateDocument_StoresFileThroughIFileStorage_NotDisk()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("stored via the abstraction");
        var dto = UploadDto("via-storage.txt", content);

        var result = await _service.CreateDocumentAsync(dto, ContractId, tenantId: 1, "tester@example.com");

        result.FileName.Should().Be("via-storage.txt");
        result.FileSize.Should().Be(content.Length);

        // The file must live in the in-memory storage at the canonical path...
        var expectedPath = _fileStorage.GenerateFilePath(ContractId, "via-storage.txt", 1);
        _fileStorage.StoredFiles.Should().ContainKey(expectedPath);
        (await _fileStorage.ReadAsync(expectedPath)).Should().Equal(content);

        // ...and the DB row must reference it.
        var row = await _context.Documents.AsNoTracking().FirstAsync(d => d.Id == result.Id);
        row.FilePath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task GetDocumentContent_ReadsThroughIFileStorage()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("download bytes");
        var created = await _service.CreateDocumentAsync(UploadDto("download.txt", content), ContractId, 1, null);

        var bytes = await _service.GetDocumentContentAsync(created.Id, tenantId: 1);

        bytes.Should().Equal(content);
    }

    [Fact]
    public async Task GetDocumentContent_FileAlreadyGone_ThrowsKeyNotFound()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("orphan metadata");
        var created = await _service.CreateDocumentAsync(UploadDto("orphan.txt", content), ContractId, 1, null);

        // Simulate storage loss: metadata row exists, the file does not.
        await _fileStorage.DeleteAsync(_fileStorage.GenerateFilePath(ContractId, "orphan.txt", 1));

        var act = () => _service.GetDocumentContentAsync(created.Id, tenantId: 1);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateDocument_OverStorageMaxSize_ThrowsInvalidOperation()
    {
        // Storage double is configured with a 1 KB limit; 2 KB must be
        // rejected by the storage layer, not by any check in the service.
        var tooBig = new byte[2 * 1024];

        var act = () => _service.CreateDocumentAsync(UploadDto("big.bin", tooBig), ContractId, 1, null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeds maximum allowed size*");
        _fileStorage.StoredFiles.Should().BeEmpty();
        (await _context.Documents.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteDocument_SoftDelete_KeepsFileInStorage()
    {
        // Issue #14 semantics: soft delete keeps the file for the retention
        // purge, which removes it through IFileStorage.DeleteAsync.
        var content = System.Text.Encoding.UTF8.GetBytes("soft delete body");
        var created = await _service.CreateDocumentAsync(UploadDto("soft.txt", content), ContractId, 1, null);

        var deleted = await _service.DeleteDocumentAsync(created.Id, tenantId: 1, "deleter@example.com");

        deleted.Should().BeTrue();
        _fileStorage.StoredFiles.Should()
            .ContainKey(_fileStorage.GenerateFilePath(ContractId, "soft.txt", 1));
    }
}
