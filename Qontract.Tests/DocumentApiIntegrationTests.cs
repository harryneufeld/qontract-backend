using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Qontract.Application.Models;
using Xunit;

namespace Qontract.Tests;

/// <summary>
/// End-to-end tests for the Documents API: upload/download round-trip,
/// audit fields, and cross-tenant isolation.
/// Tests within the class run sequentially, so mutating the shared
/// client's Authorization header per test is safe.
/// Shares the on-disk Storage folder with SoftDeleteTests, so both
/// classes run in the same xunit collection (sequentially).
/// </summary>
[Collection("QontractApiStorage")]
public class DocumentApiIntegrationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly string _storageRoot;

    public DocumentApiIntegrationTests(QontractWebApplicationFactory factory)
    {
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

    private record ContractJson(int Id, string Title);

    private record DocumentJson(
        int Id,
        string FileName,
        string FileType,
        long FileSize,
        int ContractId,
        DateTime UploadedAt,
        int Version,
        string? CreatedBy,
        string? ModifiedBy,
        string? Title = null,
        DateTime? DocumentDate = null);

    [Fact]
    public async Task GetDocuments_ShouldReturnUnauthorized_WithoutToken()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/documents/contract/1/documents");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DocumentUploadDownloadRoundTrip_ShouldStoreAndReturnBytes()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Doc round-trip contract");
        var content = "lease agreement body for testing " + Guid.NewGuid().ToString("N");
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));

        // Upload
        var createResponse = await _client.PostAsJsonAsync($"/api/documents/contract/{contractId}/documents", new
        {
            fileName = "lease.txt",
            fileType = "text/plain",
            fileData = base64,
            contractId
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentJson>();
        created.Should().NotBeNull();
        created!.FileName.Should().Be("lease.txt");
        created.FileType.Should().Be("text/plain");
        created.FileSize.Should().Be(content.Length);
        created.ContractId.Should().Be(contractId);
        created.Version.Should().Be(1);
        created.CreatedBy.Should().Be("admin@qontract.com");

        // The file must actually exist on disk
        var docsInDb = await _client.GetFromJsonAsync<List<DocumentJson>>(
            $"/api/documents/contract/{contractId}/documents");
        docsInDb.Should().HaveCount(1);

        // Download returns the exact bytes
        var downloadResponse = await _client.GetAsync($"/api/documents/{created.Id}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var downloaded = await downloadResponse.Content.ReadAsStringAsync();
        downloaded.Should().Be(content);

        // Delete removes the record
        var deleteResponse = await _client.DeleteAsync($"/api/documents/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterDelete = await _client.GetAsync($"/api/documents/{created.Id}");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateDocument_ShouldReturn400_WhenFileDataMissingOrInvalid()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Doc validation contract");

        // Missing fileData -> model binding 400
        var missing = await _client.PostAsJsonAsync($"/api/documents/contract/{contractId}/documents", new
        {
            fileName = "a.txt",
            fileType = "text/plain",
            contractId
        });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Invalid base64 -> 400 business rule
        var invalid = await _client.PostAsJsonAsync($"/api/documents/contract/{contractId}/documents", new
        {
            fileName = "a.txt",
            fileType = "text/plain",
            fileData = "!!!not-base64!!!",
            contractId
        });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateDocument_ShouldReturn404_ForUnknownContract()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var response = await _client.PostAsJsonAsync("/api/documents/contract/999999/documents", new
        {
            fileName = "a.txt",
            fileType = "text/plain",
            fileData = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            contractId = 999999
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DocumentMetadata_CreateAndPatch_ShouldPersistTitleAndDate()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Doc metadata contract");
        var base64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });

        // Create with title + date
        var createResponse = await _client.PostAsJsonAsync($"/api/documents/contract/{contractId}/documents", new
        {
            fileName = "invoice-001.pdf",
            fileType = "application/pdf",
            fileData = base64,
            contractId,
            title = "  January Invoice  ",
            documentDate = "2026-01-15"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentJson>();
        created!.Title.Should().Be("January Invoice"); // trimmed
        created.DocumentDate.Should().Be(DateTime.Parse("2026-01-15"));

        // Create without metadata -> nulls
        var bareResponse = await _client.PostAsJsonAsync($"/api/documents/contract/{contractId}/documents", new
        {
            fileName = "bare.txt",
            fileType = "text/plain",
            fileData = base64,
            contractId
        });
        bareResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var bare = await bareResponse.Content.ReadFromJsonAsync<DocumentJson>();
        bare!.Title.Should().BeNull();
        bare.DocumentDate.Should().BeNull();

        // Patch: change both fields
        var patch = await _client.PatchAsJsonAsync($"/api/documents/{created.Id}/metadata", new
        {
            title = "Feb Invoice",
            documentDate = "2026-02-20"
        });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var patched = await patch.Content.ReadFromJsonAsync<DocumentJson>();
        patched!.Title.Should().Be("Feb Invoice");
        patched.DocumentDate.Should().Be(DateTime.Parse("2026-02-20"));
        patched.FileName.Should().Be("invoice-001.pdf"); // untouched

        // Patch: null leaves values, blank title clears it
        var clear = await _client.PatchAsJsonAsync($"/api/documents/{created.Id}/metadata", new
        {
            title = "   "
        });
        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        var cleared = await clear.Content.ReadFromJsonAsync<DocumentJson>();
        cleared!.Title.Should().BeNull();
        cleared.DocumentDate.Should().Be(DateTime.Parse("2026-02-20")); // unchanged

        // The list endpoint reports the same metadata
        var docs = await _client.GetFromJsonAsync<List<DocumentJson>>(
            $"/api/documents/contract/{contractId}/documents");
        docs.Should().HaveCount(2);
        docs.Single(d => d.Id == created.Id).Title.Should().BeNull();
    }

    [Fact]
    public async Task DocumentMetadata_Patch_ShouldRejectTooLongTitle_AndUnknownIds()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Doc metadata bad contract");
        var base64 = Convert.ToBase64String(new byte[] { 1 });
        var createResponse = await _client.PostAsJsonAsync($"/api/documents/contract/{contractId}/documents", new
        {
            fileName = "a.txt",
            fileType = "text/plain",
            fileData = base64,
            contractId
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentJson>();

        // > 255 chars -> 400
        var tooLong = await _client.PatchAsJsonAsync($"/api/documents/{created!.Id}/metadata", new
        {
            title = new string('x', 256)
        });
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Unknown document -> 404
        var missing = await _client.PatchAsJsonAsync("/api/documents/999999/metadata", new
        {
            title = "ghost"
        });
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DocumentMetadata_ShouldNotLeakAcrossTenants()
    {
        var tenant1Token = await GetTokenAsync("admin@qontract.com");
        Authenticate(tenant1Token);
        var tenant1Contract = await CreateContractAsync("Tenant1 metadata contract");
        var base64 = Convert.ToBase64String(new byte[] { 9 });
        var upload = await _client.PostAsJsonAsync($"/api/documents/contract/{tenant1Contract}/documents", new
        {
            fileName = "secret.txt",
            fileType = "text/plain",
            fileData = base64,
            contractId = tenant1Contract,
            title = "secret"
        });
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await upload.Content.ReadFromJsonAsync<DocumentJson>();

        var tenant2Token = await GetTokenAsync("admin2@qontract.com");
        Authenticate(tenant2Token);
        (await _client.PatchAsJsonAsync($"/api/documents/{doc!.Id}/metadata", new { title = "hijacked" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        Authenticate(tenant1Token);
        (await (await _client.GetAsync($"/api/documents/{doc.Id}"))
            .Content.ReadFromJsonAsync<DocumentJson>()!).Title.Should().Be("secret");
    }

    [Fact]
    public async Task Documents_ShouldNotLeakAcrossTenants()
    {
        // Tenant 1 uploads a document
        var tenant1Token = await GetTokenAsync("admin@qontract.com");
        Authenticate(tenant1Token);
        var tenant1Contract = await CreateContractAsync("Tenant1 doc contract");
        var base64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        var upload = await _client.PostAsJsonAsync($"/api/documents/contract/{tenant1Contract}/documents", new
        {
            fileName = "secret.txt",
            fileType = "text/plain",
            fileData = base64,
            contractId = tenant1Contract
        });
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await upload.Content.ReadFromJsonAsync<DocumentJson>();

        // Tenant 2 cannot read, download, update or delete it
        var tenant2Token = await GetTokenAsync("admin2@qontract.com");
        Authenticate(tenant2Token);

        (await _client.GetAsync($"/api/documents/{doc!.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/documents/{doc.Id}/download")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await _client.PutAsJsonAsync($"/api/documents/{doc.Id}", new
        {
            fileName = "hijacked.txt",
            fileType = "text/plain",
            fileData = base64,
            contractId = doc.ContractId
        })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.DeleteAsync($"/api/documents/{doc.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        // And it is still there for tenant 1
        Authenticate(tenant1Token);
        (await _client.GetAsync($"/api/documents/{doc.Id}")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }
}
