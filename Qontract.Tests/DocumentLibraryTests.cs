using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qontract.Application.Models;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

[Collection("QontractApiStorage")]
public class DocumentLibraryTests(QontractWebApplicationFactory factory) : IClassFixture<QontractWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    private async Task<(HttpClient Client, AuthDTO Auth)> Login(string email = "admin@qontract.com")
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }
    private async Task<HttpClient> Peer()
    {
        // Even another administrator in the same tenant must not read personal files.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = await db.Users.SingleAsync(u => u.Email == "admin@qontract.com");
        var email = Guid.NewGuid() + "@example.com";
        db.Users.Add(new User { Name = "Peer", Email = email, TenantId = admin.TenantId,
            PasswordHash = admin.PasswordHash, Role = UserRole.TenantAdmin, IsActive = true });
        await db.SaveChangesAsync();
        return (await Login(email)).Client;
    }
    private static object UploadBody(int? spoofedUserId = null) => new
    {
        fileName = "Library.pdf", fileType = "application/pdf", fileData = Convert.ToBase64String("Evidence bytes"u8),
        title = "Original title", category = "Receipts", description = "Original description", documentDate = "2026-10-01T00:00:00Z",
        userId = spoofedUserId, tenantId = -1, contractId = -1
    };
    private static async Task<DocumentDTO> Upload(HttpClient client, string scope, int? userId = null)
    {
        var response = await client.PostAsJsonAsync($"/api/documents?scope={scope}", UploadBody(userId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
    }
    private static async Task<int> Contract(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/contracts", new { title = "Library contract", status = "Active" });
        return (await response.Content.ReadFromJsonAsync<ContractDTO>(Json))!.Id;
    }

    [Fact]
    public async Task PersonalFilesAreOwnerOnlyAcrossEveryEndpoint_EvenForTenantAdmins()
    {
        var (owner, auth) = await Login(); using var peer = await Peer(); using var foreign = (await Login("admin2@qontract.com")).Client;
        var doc = await Upload(owner, "Personal", -1);
        doc.UserId.Should().Be(auth.UserId); doc.TenantId.Should().BeNull(); doc.ContractId.Should().BeNull();
        doc.Scope.Should().Be("Personal"); doc.Category.Should().Be("Receipts"); doc.Description.Should().Be("Original description");
        var contract = await Contract(owner);
        (await owner.PostAsync($"/api/documents/{doc.Id}/contracts/{contract}", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync("/api/invoices", new { invoiceDate = "2026-10-01", amount = "1", documentId = doc.Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var other in new[] { peer, foreign })
        {
            foreach (var path in new[] { $"/api/documents/{doc.Id}", $"/api/documents/{doc.Id}/download", $"/api/documents/{doc.Id}/versions" })
                (await other.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.PutAsJsonAsync($"/api/documents/{doc.Id}", new { fileName = "a", fileType = "text/plain", fileData = "YQ==", contractId = 1 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.PatchAsJsonAsync($"/api/documents/{doc.Id}/metadata", new { title = "Intrusion" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.PostAsJsonAsync($"/api/documents/{doc.Id}/reupload", new { fileData = "YQ==" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.DeleteAsync($"/api/documents/{doc.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.PostAsync($"/api/documents/{doc.Id}/contracts/{contract}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await other.GetFromJsonAsync<List<DocumentDTO>>("/api/documents?scope=Personal", Json))!.Should().NotContain(d => d.Id == doc.Id);
            (await other.GetFromJsonAsync<List<DocumentDTO>>("/api/documents?scope=Tenant", Json))!.Should().NotContain(d => d.Id == doc.Id);
        }
        (await owner.DeleteAsync($"/api/documents/{doc.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var other in new[] { peer, foreign })
        {
            (await other.GetFromJsonAsync<List<DocumentDTO>>("/api/documents/trash", Json))!.Should().NotContain(d => d.Id == doc.Id);
            (await other.PostAsync($"/api/documents/{doc.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        (await owner.PostAsync($"/api/documents/{doc.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetByteArrayAsync($"/api/documents/{doc.Id}/download")).Should().Equal("Evidence bytes"u8.ToArray());
    }

    [Fact]
    public async Task TenantFilesNeedNoContract_AreSharedWithinTenant_AndRetainOriginalOwnerOnReupload()
    {
        var (owner, auth) = await Login(); using var peer = await Peer(); using var foreign = (await Login("admin2@qontract.com")).Client;
        var doc = await Upload(owner, "Tenant", -1);
        doc.UserId.Should().Be(auth.UserId); doc.ContractId.Should().BeNull(); doc.TenantId.Should().NotBeNull();
        (await peer.GetAsync($"/api/documents/{doc.Id}/download")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await foreign.GetAsync($"/api/documents/{doc.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var contract = await Contract(owner);
        (await peer.PostAsync($"/api/documents/{doc.Id}/contracts/{contract}", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await peer.GetFromJsonAsync<List<DocumentDTO>>($"/api/documents/contract/{contract}/documents", Json))!.Should().Contain(d => d.Id == doc.Id);
        (await peer.PostAsJsonAsync("/api/invoices", new { invoiceDate = "2026-10-01", amount = "1", documentId = doc.Id })).StatusCode.Should().Be(HttpStatusCode.Created);
        var response = await peer.PostAsJsonAsync($"/api/documents/{doc.Id}/reupload", new { fileData = "YQ==", fileName = "revision.pdf" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = (await response.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        current.UserId.Should().Be(auth.UserId); current.ContractIds.Should().Contain(contract);
        current.Scope.Should().Be("Tenant"); current.Description.Should().Be(doc.Description); current.Category.Should().Be(doc.Category);
        (await owner.GetFromJsonAsync<List<DocumentDTO>>($"/api/documents/{current.Id}/versions", Json))!.Select(d => d.Id).Should().BeEquivalentTo(new[] { doc.Id, current.Id });
        (await peer.GetByteArrayAsync($"/api/documents/{doc.Id}/download")).Should().Equal("Evidence bytes"u8.ToArray());
        (await peer.DeleteAsync($"/api/documents/{current.Id}/contracts/{contract}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await peer.GetAsync($"/api/documents/{current.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PersonalFilesFollowAccountAcrossTenantSwitch_WithoutExposingPreviousTenantFiles()
    {
        var (client, auth) = await Login();
        var personal = await Upload(client, "Personal"); var shared = await Upload(client, "Tenant");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Users.Where(u => u.Email == "admin2@qontract.com").Select(u => u.TenantId).SingleAsync();
        if (!await db.TenantMemberships.AnyAsync(m => m.UserId == auth.UserId && m.TenantId == tenant))
        {
            db.TenantMemberships.Add(new TenantMembership { UserId = auth.UserId, TenantId = tenant!.Value, IsActive = true, Role = UserRole.User }); await db.SaveChangesAsync();
        }
        var switchResponse = await client.PostAsJsonAsync("/api/auth/switch-tenant", new { tenantId = tenant });
        switchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var switched = (await switchResponse.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", switched.AccessToken);
        (await client.GetAsync($"/api/documents/{personal.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<List<DocumentDTO>>("/api/documents?scope=Personal", Json))!.Should().Contain(d => d.Id == personal.Id);
        (await client.GetAsync($"/api/documents/{shared.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PersonalReuploadMetadataAndTrashRoundTripWithoutContract()
    {
        var (client, auth) = await Login(); var doc = await Upload(client, "Personal");
        (await client.PatchAsJsonAsync($"/api/documents/{doc.Id}/metadata", new { description = "Changed", category = "", userId = -1 })).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await client.PostAsJsonAsync($"/api/documents/{doc.Id}/reupload", new { fileData = "YQ==" });
        var current = (await response.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        current.UserId.Should().Be(auth.UserId); current.Category.Should().BeNull(); current.Description.Should().Be("Changed");
        (await client.DeleteAsync($"/api/documents/{current.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<List<DocumentDTO>>("/api/documents?scope=Personal&deleted=true", Json))!.Should().Contain(d => d.Id == current.Id);
        (await client.PostAsync($"/api/documents/{current.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LongStandaloneHistoryIsComplete_AndOldIdReuploadResolvesLatestVersion()
    {
        var (client, auth) = await Login(); var first = await Upload(client, "Personal");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var original = (await db.Documents.FindAsync(first.Id))!;
        var revisions = Enumerable.Range(2, 105).Select(version => new Document
        {
            Scope = DocumentScope.Personal, UserId = auth.UserId, FileName = "history.pdf", FileType = "application/pdf",
            FilePath = original.FilePath, FileSize = original.FileSize, Version = version, UploadedBy = "Historical actor"
        }).ToList();
        db.Documents.AddRange(revisions); await db.SaveChangesAsync();
        var chain = new[] { original }.Concat(revisions).ToList();
        for (var i = 0; i < chain.Count - 1; i++) chain[i].SupersededById = chain[i + 1].Id;
        await db.SaveChangesAsync();
        var history = (await client.GetFromJsonAsync<List<DocumentDTO>>($"/api/documents/{first.Id}/versions", Json))!;
        history.Should().HaveCount(106); history.Last().Version.Should().Be(106);
        var response = await client.PostAsJsonAsync($"/api/documents/{first.Id}/reupload", new { fileData = "YQ==" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = (await response.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        current.Version.Should().Be(107); current.UserId.Should().Be(auth.UserId);
    }
}
