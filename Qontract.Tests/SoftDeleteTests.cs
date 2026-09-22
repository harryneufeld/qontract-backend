using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Qontract.Application.Models;
using Qontract.Application.Services;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

/// <summary>
/// Tests for soft deletion and the document deletion queue (issue #14):
/// DELETE soft-deletes (row + file kept, hidden from all endpoints), and
/// the retention purge permanently removes soft-deleted documents once
/// their retention period passes. Retention resolution: per-tenant
/// setting (Tenant.Settings) &gt; global config (default 90 days).
///
/// Shares the on-disk Storage folder with DocumentApiIntegrationTests, so
/// both classes run in the same xunit collection (sequentially).
/// </summary>
[Collection("QontractApiStorage")]
public class SoftDeleteTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _storageRoot;

    public SoftDeleteTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _storageRoot = Path.GetFullPath("Storage");
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthDTO>();
        auth.Should().NotBeNull();
        return auth!.AccessToken;
    }

    private void Authenticate(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<int> CreateContractAsync(string title)
    {
        var response = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title,
            status = "Active"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ContractJson>();
        created.Should().NotBeNull();
        return created!.Id;
    }

    private async Task<(int Id, string FilePath)> UploadAndSoftDeleteAsync(string fileName, string content)
    {
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));
        var contractResponse = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = $"Soft delete contract {fileName}",
            status = "Active"
        });
        contractResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var contract = await contractResponse.Content.ReadFromJsonAsync<ContractJson>();

        var createResponse = await _client.PostAsJsonAsync($"/api/documents/contract/{contract!.Id}/documents", new
        {
            fileName,
            fileType = "text/plain",
            fileData = base64,
            contractId = contract.Id
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentJson>();

        var deleteResponse = await _client.DeleteAsync($"/api/documents/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Documents.FirstAsync(d => d.Id == created.Id);
        return (created.Id, row.FilePath);
    }

    private record ContractJson(int Id, string Title);

    private record DocumentJson(int Id, string FileName, string FileType, long FileSize, int ContractId);

    [Fact]
    public async Task DeleteDocument_ShouldSoftDelete_HideFromAllEndpoints_KeepRowAndFile()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var (docId, filePath) = await UploadAndSoftDeleteAsync(
            "soft-delete.txt", "document body for soft delete " + Guid.NewGuid().ToString("N"));

        // Gone from every read path...
        (await _client.GetAsync($"/api/documents/{docId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/documents/{docId}/download")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        // ...and from the contract's document list (DB view of what the
        // list endpoint returns)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Documents.FirstAsync(d => d.Id == docId);
        var list = await db.Documents
            .Where(d => d.ContractId == row.ContractId && !d.IsDeleted)
            .ToListAsync();
        list.Should().BeEmpty();

        // ...but the row and the file are still there for the retention purge
        row.IsDeleted.Should().BeTrue();
        row.DeletedAt.Should().BeOnOrBefore(DateTime.UtcNow);
        row.DeletedAt.Should().BeOnOrAfter(DateTime.UtcNow.AddMinutes(-5));
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteDocument_Again_ShouldReturn404_WhenAlreadySoftDeleted()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var (docId, _) = await UploadAndSoftDeleteAsync(
            "double-delete.txt", "double delete body " + Guid.NewGuid().ToString("N"));

        (await _client.DeleteAsync($"/api/documents/{docId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Purge_ShouldRemoveRowAndFile_WhenGlobalRetentionExpired()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var (docId, filePath) = await UploadAndSoftDeleteAsync(
            "expired.txt", "expired body " + Guid.NewGuid().ToString("N"));

        // Age the soft delete beyond the 90-day default retention
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Documents.FirstAsync(d => d.Id == docId);
        row.DeletedAt = DateTime.UtcNow.AddDays(-91);
        await db.SaveChangesAsync();

        var purged = await RunPurgeAsync();
        purged.Should().BeGreaterThanOrEqualTo(1);

        var after = await db.Documents.AnyAsync(d => d.Id == docId);
        after.Should().BeFalse();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task Purge_ShouldKeepDocument_WhileWithinRetention()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var (docId, filePath) = await UploadAndSoftDeleteAsync(
            "fresh.txt", "fresh body " + Guid.NewGuid().ToString("N"));

        // DeletedAt is "now" (stamped by the API): well within the 90-day default
        var purged = await RunPurgeAsync();
        purged.Should().Be(0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Documents.AnyAsync(d => d.Id == docId)).Should().BeTrue();
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task Purge_ShouldApplyTenantRetentionSetting_OverGlobalDefault()
    {
        var token1 = await GetTokenAsync("admin@qontract.com");
        var token2 = await GetTokenAsync("admin2@qontract.com");

        // Tenant 2 shortens its retention to 1 day
        using (var settingsScope = _factory.Services.CreateScope())
        {
            var settingsDb = settingsScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant2 = await settingsDb.Tenants.FirstAsync(t => t.Name == "Test Family Two");
            tenant2.Settings = "{\"documentRetentionDays\": 1}";
            await settingsDb.SaveChangesAsync();
        }

        // Tenant 2: soft-delete a doc 2 days ago -> past its 1-day retention
        Authenticate(token2);
        var (tenant2DocId, tenant2FilePath) = await UploadAndSoftDeleteAsync(
            "tenant-short.txt", "tenant short retention " + Guid.NewGuid().ToString("N"));

        // Tenant 1 (default 90 days): soft-delete a doc 2 days ago -> still retained
        Authenticate(token1);
        var (tenant1DocId, tenant1FilePath) = await UploadAndSoftDeleteAsync(
            "tenant-default.txt", "tenant default retention " + Guid.NewGuid().ToString("N"));

        using (var ageScope = _factory.Services.CreateScope())
        {
            var ageDb = ageScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var t1 = await ageDb.Documents.FirstAsync(d => d.Id == tenant1DocId);
            var t2 = await ageDb.Documents.FirstAsync(d => d.Id == tenant2DocId);
            t1.DeletedAt = DateTime.UtcNow.AddDays(-2);
            t2.DeletedAt = DateTime.UtcNow.AddDays(-2);
            await ageDb.SaveChangesAsync();
        }

        await RunPurgeAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Tenant 2's document (1-day retention) is purged...
        (await db.Documents.AnyAsync(d => d.Id == tenant2DocId)).Should().BeFalse();
        File.Exists(tenant2FilePath).Should().BeFalse();

        // ...tenant 1's document (90-day default) is kept
        (await db.Documents.AnyAsync(d => d.Id == tenant1DocId)).Should().BeTrue();
        File.Exists(tenant1FilePath).Should().BeTrue();
    }

    private async Task<int> RunPurgeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var purger = scope.ServiceProvider.GetRequiredService<DocumentPurgeService>();
        return await purger.PurgeExpiredAsync();
    }

    [Fact]
    public void TenantSettingsParser_ShouldReadDocumentRetentionDays_FromValidJson()
    {
        TenantSettingsParser.GetDocumentRetentionDays("{\"documentRetentionDays\": 30}")
            .Should().Be(30);
    }

    [Fact]
    public void TenantSettingsParser_ShouldReturnNull_ForMissingKeyMalformedOrInvalidValues()
    {
        TenantSettingsParser.GetDocumentRetentionDays(null).Should().BeNull();
        TenantSettingsParser.GetDocumentRetentionDays("").Should().BeNull();
        TenantSettingsParser.GetDocumentRetentionDays("{}").Should().BeNull();
        TenantSettingsParser.GetDocumentRetentionDays("not json").Should().BeNull();
        TenantSettingsParser.GetDocumentRetentionDays("{\"documentRetentionDays\": -5}").Should().BeNull();
        TenantSettingsParser.GetDocumentRetentionDays("{\"documentRetentionDays\": 0}").Should().BeNull();
        TenantSettingsParser.GetDocumentRetentionDays("{\"documentRetentionDays\": \"abc\"}").Should().BeNull();
        TenantSettingsParser.GetDocumentRetentionDays("[1, 2]").Should().BeNull();
    }
}
