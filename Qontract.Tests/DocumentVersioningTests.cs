using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Qontract.Application.Models;
using Xunit;

namespace Qontract.Tests;

/// <summary>
/// End-to-end tests for document versioning (issue #23): re-upload
/// increments the version, old versions are retained, listable via
/// GET /api/documents/{id}/versions, and individually downloadable.
/// Shares the on-disk Storage folder with the other document tests, so it
/// runs in the same xunit collection (sequentially).
/// </summary>
[Collection("QontractApiStorage")]
public class DocumentVersioningTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DocumentVersioningTests(QontractWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record ContractJson(int Id, string Title);

    private record VersionJson(int Id, string FileName, long FileSize, int Version, string? Title = null);

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

    private async Task<VersionJson> UploadAsync(int contractId, string fileName, string content)
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
        var created = await response.Content.ReadFromJsonAsync<VersionJson>();
        created.Should().NotBeNull();
        return created!;
    }

    private async Task<VersionJson> ReuploadAsync(int documentId, string content, string? fileName = null, string? title = null)
    {
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));
        var response = await _client.PostAsJsonAsync($"/api/documents/{documentId}/reupload", new
        {
            fileData = base64,
            fileName,
            title
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, response.Content.ReadAsStringAsync().Result);
        var created = await response.Content.ReadFromJsonAsync<VersionJson>();
        created.Should().NotBeNull();
        return created!;
    }

    private static string ToBase64(string content) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task Reupload_ShouldIncrementVersion_RetainAndServeOldVersion()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Versioning contract");
        var v1 = await UploadAsync(contractId, "lease.txt", "version one content");

        var v2 = await ReuploadAsync(v1.Id, "version two content");

        // New version: new id, incremented number, new file size.
        v2.Id.Should().NotBe(v1.Id);
        v2.Version.Should().Be(2);
        v2.FileName.Should().Be("lease.txt"); // name kept when not overridden
        v2.FileSize.Should().Be("version two content".Length);

        // The contract's document list shows the logical document once, at
        // its latest version — the superseded v1 is not a separate entry.
        var list = await _client.GetFromJsonAsync<List<VersionJson>>(
            $"/api/documents/contract/{contractId}/documents");
        list.Should().HaveCount(1);
        list![0].Id.Should().Be(v2.Id);
        list[0].Version.Should().Be(2);

        // The version history lists both versions, oldest first — querying
        // by either version's id yields the same family.
        var versionsFromOld = await _client.GetFromJsonAsync<List<VersionJson>>($"/api/documents/{v1.Id}/versions");
        versionsFromOld.Should().HaveCount(2);
        versionsFromOld![0].Id.Should().Be(v1.Id);
        versionsFromOld[0].Version.Should().Be(1);
        versionsFromOld[1].Id.Should().Be(v2.Id);
        versionsFromOld[1].Version.Should().Be(2);

        var versionsFromNew = await _client.GetFromJsonAsync<List<VersionJson>>($"/api/documents/{v2.Id}/versions");
        versionsFromNew.Should().BeEquivalentTo(versionsFromOld, o => o.ExcludingMissingMembers());

        // Each version stays individually downloadable with its own bytes.
        var oldBytes = await _client.GetStringAsync($"/api/documents/{v1.Id}/download");
        oldBytes.Should().Be("version one content");
        var newBytes = await _client.GetStringAsync($"/api/documents/{v2.Id}/download");
        newBytes.Should().Be("version two content");
    }

    [Fact]
    public async Task Reupload_Again_ShouldReachVersion3_AndAcceptSupersededId()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Versioning triple contract");
        var v1 = await UploadAsync(contractId, "invoice.txt", "v1");
        var v2 = await ReuploadAsync(v1.Id, "v2");

        // Referencing the already-superseded v1 id resolves forward to the
        // current version, so the re-upload still targets the same family.
        var v3 = await ReuploadAsync(v1.Id, "v3", fileName: "invoice-v3.txt");
        v3.Version.Should().Be(3);
        v3.FileName.Should().Be("invoice-v3.txt");

        var versions = await _client.GetFromJsonAsync<List<VersionJson>>($"/api/documents/{v2.Id}/versions");
        versions.Select(v => v.Version).Should().Equal(1, 2, 3);

        var oldBytes = await _client.GetStringAsync($"/api/documents/{v1.Id}/download");
        oldBytes.Should().Be("v1");
    }

    [Fact]
    public async Task Reupload_ShouldKeepMetadata_AndApplyOverrides()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Versioning metadata contract");
        var v1 = await _client.PostAsJsonAsync($"/api/documents/contract/{contractId}/documents", new
        {
            fileName = "doc.txt",
            fileType = "text/plain",
            fileData = ToBase64("first"),
            contractId,
            title = "First draft",
            documentDate = "2026-01-01"
        });
        v1.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await v1.Content.ReadFromJsonAsync<VersionJson>();
        created!.Title.Should().Be("First draft");

        // No overrides: previous title and date are carried over.
        var v2 = await ReuploadAsync(created.Id, "second");
        var v2Full = await _client.GetFromJsonAsync<FullJson>($"/api/documents/{v2.Id}");
        v2Full!.Title.Should().Be("First draft");
        v2Full.DocumentDate.Should().Be(DateTime.Parse("2026-01-01"));

        // Explicit overrides apply to the new version only.
        var v3 = await ReuploadAsync(created.Id, "third", title: "Final");
        var v3Full = await _client.GetFromJsonAsync<FullJson>($"/api/documents/{v3.Id}");
        v3Full!.Title.Should().Be("Final");
        v3Full.DocumentDate.Should().Be(DateTime.Parse("2026-01-01")); // carried, not reset
    }

    private record FullJson(int Id, int Version, string? Title, DateTime? DocumentDate);

    [Fact]
    public async Task Reupload_ShouldReturn404_ForUnknownOrOtherTenantDocument()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var unknown = await _client.PostAsJsonAsync("/api/documents/999999/reupload", new
        {
            fileData = ToBase64("x")
        });
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A document of the other tenant is invisible to this user.
        var contractId = await CreateContractAsync("Cross-tenant versioning contract");
        var v1 = await UploadAsync(contractId, "secret.txt", "tenant one");

        var otherToken = await GetTokenAsync("admin2@qontract.com");
        Authenticate(otherToken);
        var other = await _client.PostAsJsonAsync($"/api/documents/{v1.Id}/reupload", new
        {
            fileData = ToBase64("x")
        });
        other.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var versions = await _client.GetAsync($"/api/documents/{v1.Id}/versions");
        versions.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reupload_ShouldReturn400_WhenFileDataMissingOrInvalid()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        Authenticate(token);

        var contractId = await CreateContractAsync("Versioning validation contract");
        var v1 = await UploadAsync(contractId, "val.txt", "a");

        // Missing fileData -> 400 via the validator.
        var missing = await _client.PostAsJsonAsync($"/api/documents/{v1.Id}/reupload", new
        {
            fileName = "val.txt"
        });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Invalid base64 -> 400 business rule.
        var invalid = await _client.PostAsJsonAsync($"/api/documents/{v1.Id}/reupload", new
        {
            fileData = "!!!not-base64!!!"
        });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The failed re-upload must not have changed the document.
        var versions = await _client.GetFromJsonAsync<List<VersionJson>>($"/api/documents/{v1.Id}/versions");
        versions.Should().HaveCount(1);
        versions![0].Version.Should().Be(1);
    }
}
