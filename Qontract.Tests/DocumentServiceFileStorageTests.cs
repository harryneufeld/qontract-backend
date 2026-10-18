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


    [Fact]
    public async Task UploadUpdatesOnlyItsContractAuditTimestampAndActor()
    {
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var id = ContractId;
        _context.Contracts.Add(new Contract { Title = "Other contract", TenantId = 1, Status = ContractStatus.Active, IsActive = true });
        await _context.SaveChangesAsync();
        await _context.Contracts.ExecuteUpdateAsync(s => s.SetProperty(c => c.ModifiedAt, old));
        _context.ChangeTracker.Clear();
        var before = DateTime.UtcNow;
        await _service.CreateDocumentAsync(UploadDto("new.txt", "content"u8.ToArray()), id, 1, "uploader");
        var contract = await _context.Contracts.AsNoTracking().SingleAsync(c => c.Id == id);
        contract.ModifiedAt.Should().BeOnOrAfter(before);
        contract.ModifiedBy.Should().Be("uploader");
        (await _context.Contracts.AsNoTracking().SingleAsync(c => c.Id != id)).ModifiedAt.Should().Be(old);
    }

    [Fact]
    public async Task RejectedUploadDoesNotChangeContractAudit()
    {
        var id = ContractId;
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _context.Contracts.ExecuteUpdateAsync(s => s.SetProperty(c => c.ModifiedAt, old));
        _context.ChangeTracker.Clear();
        var action = () => _service.CreateDocumentAsync(UploadDto("large.txt", new byte[2048]), id, 1, "uploader");
        await action.Should().ThrowAsync<InvalidOperationException>();
        (await _context.Contracts.AsNoTracking().SingleAsync()).ModifiedAt.Should().Be(old);
    }

    [Fact]
    public async Task LinkAndReuploadUpdateAffectedContractsButRepeatedLinkDoesNot()
    {
        var anchor = ContractId;
        _context.Contracts.Add(new Contract { Title = "Linked contract", TenantId = 1, Status = ContractStatus.Active, IsActive = true });
        await _context.SaveChangesAsync();
        var linked = await _context.Contracts.Where(c => c.Id != anchor).Select(c => c.Id).SingleAsync();
        var doc = await _service.CreateDocumentAsync(UploadDto("version.txt", "one"u8.ToArray()), anchor, 1, "creator");
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _context.Contracts.ExecuteUpdateAsync(s => s.SetProperty(c => c.ModifiedAt, old));
        _context.ChangeTracker.Clear();
        await _service.SetContractLinkAsync(doc.Id, linked, 1, true, "linker");
        var updated = await _context.Contracts.AsNoTracking().SingleAsync(c => c.Id == linked);
        updated.ModifiedAt.Should().BeAfter(old);
        updated.ModifiedBy.Should().Be("linker");
        var linkTime = updated.ModifiedAt;
        await _service.SetContractLinkAsync(doc.Id, linked, 1, true, "repeat");
        (await _context.Contracts.AsNoTracking().SingleAsync(c => c.Id == linked)).ModifiedAt.Should().Be(linkTime);
        var before = DateTime.UtcNow;
        await _service.ReuploadDocumentAsync(doc.Id, new DocumentReuploadDTO { FileData = Convert.ToBase64String("two"u8) }, 1, "replacer");
        foreach (var contract in await _context.Contracts.AsNoTracking().ToListAsync()) {
            contract.ModifiedAt.Should().BeOnOrAfter(before);
            contract.ModifiedBy.Should().Be("replacer");
        }
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
        var expectedPath = _fileStorage.StoredFiles.Keys.Single();
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
        await _fileStorage.DeleteAsync((await _context.Documents.FindAsync(created.Id))!.FilePath);

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
            .ContainKey((await _context.Documents.FindAsync(created.Id))!.FilePath);
    }
}
