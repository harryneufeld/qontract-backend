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
using Qontract.Infrastructure;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

[Collection("QontractApiStorage")]
public class ContractReviewIntegrationTests(QontractWebApplicationFactory factory) : IClassFixture<QontractWebApplicationFactory>
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
    private static async Task<ContractDTO> Create(HttpClient client, object body)
    {
        var result = await client.PostAsJsonAsync("/api/contracts", body);
        result.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await result.Content.ReadFromJsonAsync<ContractDTO>(Json))!;
    }

    [Fact]
    public async Task InactiveContractsRemainAccessibleAndTrashCanBeRestoredWithDocuments()
    {
        var (client, _) = await Login();
        var contract = await Create(client, new { title = "Inactive lifecycle", status = "Active" });
        var upload = await client.PostAsJsonAsync($"/api/documents/contract/{contract.Id}/documents", new { fileName = "inactive.pdf", fileType = "application/pdf", fileData = Convert.ToBase64String("Evidence"u8) });
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var document = (await upload.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        (await client.PatchAsync($"/api/contracts/{contract.Id}/deactivate", new StringContent(""))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/contracts/{contract.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<List<ContractDTO>>("/api/contracts?status=inactive", Json))!.Should().Contain(c => c.Id == contract.Id);
        (await client.GetAsync($"/api/documents/{document.Id}/download")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/contracts/{contract.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/contracts/{contract.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/documents/{document.Id}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var trashed = (await client.GetFromJsonAsync<List<ContractDTO>>("/api/contracts?trash=true", Json))!.Single(c => c.Id == contract.Id);
        trashed.RetentionExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(89));
        var (other, _) = await Login("admin2@qontract.com");
        (await other.PostAsync($"/api/contracts/{contract.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var restored = await client.PostAsync($"/api/contracts/{contract.Id}/restore", null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK);
        (await restored.Content.ReadFromJsonAsync<ContractDTO>(Json))!.IsActive.Should().BeFalse();
        (await client.GetAsync($"/api/documents/{document.Id}/download")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResponsibilityIsTenantBoundAndDashboardScopeFollowsReassignment()
    {
        var (client, auth) = await Login();
        int peerId, outsideId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var admin = await db.Users.SingleAsync(u => u.Id == auth.UserId);
            var peer = new User { Name = "Responsible peer", Email = Guid.NewGuid() + "@example.test", TenantId = admin.TenantId, PasswordHash = admin.PasswordHash, Role = UserRole.User, IsActive = true };
            db.Users.Add(peer); await db.SaveChangesAsync(); peerId = peer.Id;
            outsideId = (await db.Users.SingleAsync(u => u.Email == "admin2@qontract.com")).Id;
        }
        var members = await client.GetFromJsonAsync<List<ResponsibleMemberDTO>>("/api/contracts/members", Json);
        members!.Should().Contain(m => m.Id == peerId).And.NotContain(m => m.Id == outsideId);
        var contract = await Create(client, new { title = "Assigned to peer", status = "Active", responsibleUserId = peerId });
        contract.OwnerUserId.Should().BeNull();
        (await client.GetFromJsonAsync<List<ContractDTO>>("/api/contracts?scope=mine", Json))!.Should().NotContain(c => c.Id == contract.Id);
        (await client.GetFromJsonAsync<List<ContractDTO>>("/api/contracts?scope=tenant", Json))!.Should().Contain(c => c.Id == contract.Id);
        var before = await client.GetFromJsonAsync<ContractStatsDTO>("/api/contracts/stats", Json);
        (await client.PutAsJsonAsync($"/api/contracts/{contract.Id}", new { responsibleUserId = outsideId })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync($"/api/contracts/{contract.Id}", new { responsibleUserId = auth.UserId })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<ContractStatsDTO>("/api/contracts/stats", Json))!.Total.Should().Be(before!.Total + 1);
        (await client.GetFromJsonAsync<List<ContractDTO>>("/api/contracts?scope=mine", Json))!.Should().Contain(c => c.Id == contract.Id);
        (await client.PutAsJsonAsync($"/api/contracts/{contract.Id}", new { clearResponsibleUser = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<List<ContractDTO>>("/api/contracts?scope=mine", Json))!.Should().NotContain(c => c.Id == contract.Id);
    }

    [Fact]
    public async Task PurgeKeepsSharedDocumentsAndRetentionChangesDoNotShortenExistingDeadlines()
    {
        var (client, auth) = await Login();
        var first = await Create(client, new { title = "Trash anchor", status = "Active" });
        var second = await Create(client, new { title = "Surviving shared contract", status = "Active" });
        var upload = await client.PostAsJsonAsync($"/api/documents/contract/{first.Id}/documents", new { fileName = "shared.pdf", fileType = "application/pdf", fileData = Convert.ToBase64String("Shared bytes"u8) });
        var document = (await upload.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        (await client.PostAsync($"/api/documents/{document.Id}/contracts/{second.Id}", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/contracts/{first.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Contracts.SingleAsync(c => c.Id == first.Id);
        var deadline = row.RetentionExpiresAt;
        (await client.PutAsJsonAsync($"/api/tenants/{auth.User!.TenantId}", new { settings = "{\"contractRetentionDays\":1}" })).StatusCode.Should().Be(HttpStatusCode.OK);
        row.RetentionExpiresAt.Should().Be(deadline);
        var next = await Create(client, new { title = "New short retention", status = "Active" });
        await client.DeleteAsync($"/api/contracts/{next.Id}");
        (await db.Contracts.AsNoTracking().SingleAsync(c => c.Id == next.Id)).RetentionExpiresAt.Should().BeBefore(DateTime.UtcNow.AddDays(2));
        row.RetentionExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        (await client.PostAsync($"/api/contracts/{first.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await scope.ServiceProvider.GetRequiredService<ContractPurgeService>().PurgeExpiredAsync()).Should().Be(1);
        (await db.Contracts.AnyAsync(c => c.Id == first.Id)).Should().BeFalse();
        (await db.ContractPrices.AnyAsync(p => p.ContractId == first.Id)).Should().BeFalse();
        (await client.GetAsync($"/api/documents/{document.Id}/download")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await scope.ServiceProvider.GetRequiredService<ContractPurgeService>().PurgeExpiredAsync()).Should().Be(0);
        (await client.PutAsJsonAsync($"/api/tenants/{auth.User.TenantId}", new { settings = "{\"contractRetentionDays\":0}" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await client.PutAsJsonAsync($"/api/tenants/{auth.User.TenantId}", new { settings = "{}" });
    }
    [Fact]
    public async Task PurgeQueuesExclusiveDocumentsButRetainsInvoiceContracts()
    {
        var (client, auth) = await Login();
        var removable = await Create(client, new { title = "Exclusive document cleanup", status = "Active" });
        var protectedContract = await Create(client, new { title = "Invoice retention hold", status = "Active" });
        var upload = await client.PostAsJsonAsync($"/api/documents/contract/{removable.Id}/documents", new { fileName = "exclusive.txt", fileType = "text/plain", fileData = Convert.ToBase64String("Retained bytes"u8) });
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var document = (await upload.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        await client.DeleteAsync($"/api/contracts/{removable.Id}");
        await client.DeleteAsync($"/api/contracts/{protectedContract.Id}");
        (await client.PostAsJsonAsync($"/api/documents/contract/{removable.Id}/documents", new { fileName = "rejected.txt", fileType = "text/plain", fileData = Convert.ToBase64String("No"u8) })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var row in await db.Contracts.Where(c => c.Id == removable.Id || c.Id == protectedContract.Id).ToListAsync()) row.RetentionExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        db.Invoices.Add(new Invoice { TenantId = auth.User!.TenantId, ContractId = protectedContract.Id, Amount = 10, InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow) });
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ContractPurgeService>().PurgeExpiredAsync()).Should().Be(1);
        var retained = await db.Documents.SingleAsync(d => d.Id == document.Id);
        retained.ContractId.Should().BeNull();
        retained.IsDeleted.Should().BeTrue();
        retained.DeletedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
        retained.FilePath.Should().NotBeNullOrEmpty();
        (await db.Contracts.AnyAsync(c => c.Id == protectedContract.Id)).Should().BeTrue();
    }

}
