using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qontract.Application.Models;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

/// <summary>
/// End-to-end tests for the trash view endpoints (issue #42):
/// GET /api/documents/trash lists the tenant's soft-deleted (in-retention)
/// documents, POST /api/documents/{id}/restore brings one back. Tenant
/// isolation and the 404 contract (purged / unknown / never deleted) are
/// covered, mirroring the existing soft-delete test style.
///
/// Shares the on-disk Storage folder with SoftDeleteTests, so both classes
/// run in the same xunit collection (sequentially).
/// </summary>
[Collection("QontractApiStorage")]
public class DocumentTrashTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _storageRoot;

    public DocumentTrashTests(QontractWebApplicationFactory factory)
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
        var response = await _client.PostAsJsonAsync("/api/contracts", new { title, status = "Active" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ContractJson>();
        created.Should().NotBeNull();
        return created!.Id;
    }

    private async Task<int> UploadDocumentAsync(int contractId, string fileName, string content)
    {
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));
        var response = await _client.PostAsJsonAsync($"/api/documents/contract/{contractId}/documents", new
        {
            fileName,
            fileType = "text/plain",
            fileData = base64,
            contractId
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<UploadJson>();
        return created!.Id;
    }

    private record ContractJson(int Id, string Title);

    private record UploadJson(int Id, string FileName);

    private record TrashDocumentJson(
        int Id,
        string FileName,
        int ContractId,
        DateTime? DeletedAt,
        DateTime? ExpiresAt);

    [Fact]
    public async Task TrashList_ShouldShowSoftDeletedDocs_WithDeletedAtAndExpiresAt_HideActiveAndOtherTenant()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Trash list contract");
        var content = "trash list body " + Guid.NewGuid().ToString("N");

        // Two soft-deleted documents...
        var deleted1 = await UploadDocumentAsync(contractId, "trash-1.txt", content);
        var deleted2 = await UploadDocumentAsync(contractId, "trash-2.txt", content);
        (await _client.DeleteAsync($"/api/documents/{deleted1}")).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await _client.DeleteAsync($"/api/documents/{deleted2}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // ...one active document that must not appear in the trash...
        var active = await UploadDocumentAsync(contractId, "active.txt", content);

        // ...and one soft-deleted document in tenant 2 that must not leak.
        var token2 = await GetTokenAsync("admin2@qontract.com");
        Authenticate(token2);
        var contract2 = await CreateContractAsync("Trash list contract (tenant 2)");
        var otherTenantDeleted = await UploadDocumentAsync(contract2, "other-tenant-trash.txt", content);
        (await _client.DeleteAsync($"/api/documents/{otherTenantDeleted}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // Tenant 1's trash view
        Authenticate(token);
        var response = await _client.GetAsync("/api/documents/trash");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var trash = await response.Content.ReadFromJsonAsync<List<TrashDocumentJson>>();
        // Other tests may have left soft-deleted docs behind in this shared
        // database — assert on our own documents.
        trash.Should().NotBeNull();
        trash!.Should().Contain(d => d.Id == deleted1);
        trash.Should().Contain(d => d.Id == deleted2);
        trash.Should().NotContain(d => d.Id == active);
        trash.Should().NotContain(d => d.Id == otherTenantDeleted);

        var own = trash.Where(d => d.Id == deleted1 || d.Id == deleted2).ToList();
        own.Should().HaveCount(2);
        foreach (var doc in own)
        {
            doc.DeletedAt.Should().NotBeNull();
            doc.ExpiresAt.Should().NotBeNull();
            doc.DeletedAt!.Value.Should().BeOnOrBefore(DateTime.UtcNow);
            doc.DeletedAt!.Value.Should().BeOnOrAfter(DateTime.UtcNow.AddHours(-1));
            // Default retention is 90 days.
            Math.Abs((doc.ExpiresAt!.Value - doc.DeletedAt!.Value.AddDays(90)).TotalMinutes)
                .Should().BeLessThan(1);
        }

        // Tenant 2's trash view shows its own document and never tenant 1's
        Authenticate(token2);
        var response2 = await _client.GetAsync("/api/documents/trash");
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var trash2 = await response2.Content.ReadFromJsonAsync<List<TrashDocumentJson>>();
        trash2.Should().NotBeNull();
        trash2!.Should().Contain(d => d.Id == otherTenantDeleted);
        trash2.Should().NotContain(d => d.Id == deleted1);
        trash2.Should().NotContain(d => d.Id == deleted2);
    }

    [Fact]
    public async Task TrashList_ShouldHonorTenantRetentionSetting()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        // Tenant 2 sets a 1-day retention
        using (var settingsScope = _factory.Services.CreateScope())
        {
            var settingsDb = settingsScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant2 = await settingsDb.Tenants.FirstAsync(t => t.Name == "Test Family Two");
            tenant2.Settings = "{\"documentRetentionDays\": 1}";
            await settingsDb.SaveChangesAsync();
        }

        var token2 = await GetTokenAsync("admin2@qontract.com");
        Authenticate(token2);
        var contract2 = await CreateContractAsync("Retention trash contract");
        var doc = await UploadDocumentAsync(contract2, "retention-trash.txt", "retention body " + Guid.NewGuid().ToString("N"));
        (await _client.DeleteAsync($"/api/documents/{doc}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var response = await _client.GetAsync("/api/documents/trash");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var trash = await response.Content.ReadFromJsonAsync<List<TrashDocumentJson>>();
        // Other tests may have left soft-deleted docs behind in this shared
        // database — assert on our own document.
        trash!.Should().NotBeNull();
        var entry = trash!.Should().Contain(d => d.Id == doc).Subject;
        entry.ExpiresAt.Should().NotBeNull();
        Math.Abs((entry.ExpiresAt!.Value - DateTime.UtcNow.AddDays(1)).TotalMinutes)
            .Should().BeLessThan(5);

        // Restore the tenant's default retention for the other tests
        using (var resetScope = _factory.Services.CreateScope())
        {
            var resetDb = resetScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant2 = await resetDb.Tenants.FirstAsync(t => t.Name == "Test Family Two");
            tenant2.Settings = null;
            await resetDb.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Restore_ShouldBringSoftDeletedDocumentBack()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Restore contract");
        var content = "restore body " + Guid.NewGuid().ToString("N");
        var doc = await UploadDocumentAsync(contractId, "restore.txt", content);
        (await _client.DeleteAsync($"/api/documents/{doc}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // Soft-deleted: hidden everywhere
        (await _client.GetAsync($"/api/documents/{doc}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        // Restore it
        var restoreResponse = await _client.PostAsync($"/api/documents/{doc}/restore", null);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var restored = await restoreResponse.Content.ReadFromJsonAsync<TrashDocumentJson>();
        restored!.Id.Should().Be(doc);
        restored.FileName.Should().Be("restore.txt");
        restored.DeletedAt.Should().BeNull();

        // Visible again, with the original bytes intact (soft delete never
        // removed the file)
        (await _client.GetAsync($"/api/documents/{doc}")).StatusCode
            .Should().Be(HttpStatusCode.OK);
        var download = await _client.GetAsync($"/api/documents/{doc}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsStringAsync()).Should().Be(content);

        // No longer in the trash, still in the contract list
        var trash = await _client.GetFromJsonAsync<List<TrashDocumentJson>>("/api/documents/trash");
        trash.Should().NotBeNull();
        trash!.Should().NotContain(d => d.Id == doc);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Documents.FirstAsync(d => d.Id == doc);
        row.IsDeleted.Should().BeFalse();
        row.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Restore_ShouldReturn404_WhenDocIsNotSoftDeleted()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Restore 404 contract");
        var doc = await UploadDocumentAsync(contractId, "never-deleted.txt", "never deleted " + Guid.NewGuid().ToString("N"));

        // Active (never soft-deleted) document
        (await _client.PostAsync($"/api/documents/{doc}/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        // Unknown document id
        (await _client.PostAsync("/api/documents/999999/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Restore_ShouldReturn404_AcrossTenants()
    {
        var token1 = await GetTokenAsync("admin@qontract.com");
        var token2 = await GetTokenAsync("admin2@qontract.com");

        // Tenant 2 soft-deletes a document...
        Authenticate(token2);
        var contract2 = await CreateContractAsync("Cross tenant restore contract");
        var doc = await UploadDocumentAsync(contract2, "cross-tenant.txt", "cross tenant " + Guid.NewGuid().ToString("N"));
        (await _client.DeleteAsync($"/api/documents/{doc}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // ...tenant 1 cannot restore it (uniform 404, no existence leak)
        Authenticate(token1);
        (await _client.PostAsync($"/api/documents/{doc}/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Restore_ShouldReturn404_WhenPurged()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Purged restore contract");
        var doc = await UploadDocumentAsync(contractId, "purged.txt", "purged body " + Guid.NewGuid().ToString("N"));
        (await _client.DeleteAsync($"/api/documents/{doc}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // Age the soft delete past the 90-day default retention and purge it
        using var ageScope = _factory.Services.CreateScope();
        var db = ageScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Documents.FirstAsync(d => d.Id == doc);
        row.DeletedAt = DateTime.UtcNow.AddDays(-91);
        await db.SaveChangesAsync();

        using var purgeScope = _factory.Services.CreateScope();
        var purger = purgeScope.ServiceProvider.GetRequiredService<Qontract.Application.Services.DocumentPurgeService>();
        await purger.PurgeExpiredAsync();

        (await _client.PostAsync($"/api/documents/{doc}/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TrashAndRestore_ShouldReturnUnauthorized_WithoutToken()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        (await _client.GetAsync("/api/documents/trash")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await _client.PostAsync("/api/documents/1/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
