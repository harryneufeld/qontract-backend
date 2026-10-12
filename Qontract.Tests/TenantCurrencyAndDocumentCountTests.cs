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
public class TenantCurrencyAndDocumentCountTests(QontractWebApplicationFactory factory)
    : IClassFixture<QontractWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private async Task<(HttpClient Client, AuthDTO Auth)> Login(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    private static async Task<ContractDTO> Create(HttpClient client, string title, string? currency = null)
    {
        var response = await client.PostAsJsonAsync("/api/contracts", new
        {
            title, status = "Active", currency, value = "10", billingPeriod = "Monthly"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ContractDTO>(Json))!;
    }

    [Fact]
    public async Task DefaultCurrencyAndBulkCorrectionAreAdminAndTenantScoped()
    {
        var (admin, auth) = await Login("admin@qontract.com");
        var tenantId = auth.User!.TenantId;
        var settings = JsonSerializer.Serialize(new { defaultCurrency = "GBP", contractRetentionDays = 30 });
        (await admin.PutAsJsonAsync($"/api/tenants/{tenantId}", new { settings })).StatusCode.Should().Be(HttpStatusCode.OK);
        var tenant = await admin.GetFromJsonAsync<TenantDTO>($"/api/tenants/{tenantId}", Json);
        tenant!.DefaultCurrency.Should().Be("GBP");
        var memberEmail = Guid.NewGuid() + "@example.test";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var adminUser = await db.Users.SingleAsync(u => u.Id == auth.UserId);
            var peer = new User { Name = "Currency member", Email = memberEmail,
                TenantId = tenantId, PasswordHash = adminUser.PasswordHash,
                Role = UserRole.User, IsActive = true };
            db.Users.Add(peer);
            db.TenantMemberships.Add(new TenantMembership { User = peer,
                TenantId = tenantId, Role = UserRole.User, IsActive = true });
            await db.SaveChangesAsync();
        }
        var (member, _) = await Login(memberEmail);
        var defaultResult = await member.GetFromJsonAsync<JsonElement>("/api/contracts/default-currency");
        defaultResult.GetProperty("currency").GetString().Should().Be("GBP");
        var created = await Create(member, "Uses tenant default");
        created.Currency.Should().Be("GBP");
        created.PriceHistory.Single().Currency.Should().Be("GBP");
        var missing = await Create(admin, "Imported amount");
        var explicitCurrency = await Create(admin, "Explicit currency", "USD");
        var deleted = await Create(admin, "Queued contract", "USD");
        await admin.DeleteAsync($"/api/contracts/{deleted.Id}");
        var (other, _) = await Login("admin2@qontract.com");
        var outside = await Create(other, "Other tenant", "USD");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var imported = await db.Contracts.Include(c => c.PriceHistory).SingleAsync(c => c.Id == missing.Id);
            imported.Currency = null;
            foreach (var price in imported.PriceHistory) price.Currency = null;
            await db.SaveChangesAsync();
        }
        (await member.PostAsJsonAsync($"/api/tenants/{tenantId}/apply-default-currency", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PostAsJsonAsync($"/api/tenants/{tenantId}/apply-default-currency", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var apply = await admin.PostAsJsonAsync($"/api/tenants/{tenantId}/apply-default-currency", new { });
        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        var corrected = (await admin.GetFromJsonAsync<ContractDTO>($"/api/contracts/{missing.Id}", Json))!;
        corrected.Currency.Should().Be("GBP");
        corrected.PriceHistory.Single().Currency.Should().Be("GBP");
        corrected.Value.Should().Be("10");
        (await admin.GetFromJsonAsync<ContractDTO>($"/api/contracts/{explicitCurrency.Id}", Json))!.Currency.Should().Be("USD");
        (await admin.PostAsJsonAsync($"/api/tenants/{tenantId}/apply-default-currency", new { replaceExisting = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetFromJsonAsync<ContractDTO>($"/api/contracts/{explicitCurrency.Id}", Json))!.Currency.Should().Be("GBP");
        (await other.GetFromJsonAsync<ContractDTO>($"/api/contracts/{outside.Id}", Json))!.Currency.Should().Be("USD");
        var trash = await admin.GetFromJsonAsync<List<ContractDTO>>("/api/contracts?trash=true", Json);
        trash!.Single(c => c.Id == deleted.Id).Currency.Should().Be("USD");
        (await admin.PutAsJsonAsync($"/api/tenants/{tenantId}", new { settings = "{\"defaultCurrency\":\"EURO\"}" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetFromJsonAsync<TenantDTO>($"/api/tenants/{tenantId}", Json))!.DefaultCurrency.Should().Be("GBP");
    }

    [Fact]
    public async Task CountsIncludeSharedDocumentsOnceAndExcludeTrashAndOldVersions()
    {
        var (client, _) = await Login("admin@qontract.com");
        var first = await Create(client, "Count source");
        var second = await Create(client, "Count link");
        var upload = await client.PostAsJsonAsync($"/api/documents/contract/{first.Id}/documents", new
        {
            fileName = "count.txt", fileType = "text/plain", fileData = Convert.ToBase64String("Evidence"u8)
        });
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var document = (await upload.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        (await client.PostAsync($"/api/documents/{document.Id}/contracts/{second.Id}", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<ContractDTO>($"/api/contracts/{first.Id}", Json))!.DocumentCount.Should().Be(1);
        (await client.GetFromJsonAsync<ContractDTO>($"/api/contracts/{second.Id}", Json))!.DocumentCount.Should().Be(1);
        var version = await client.PostAsJsonAsync($"/api/documents/{document.Id}/reupload", new
        {
            fileName = "count-v2.txt", fileType = "text/plain", fileData = Convert.ToBase64String("New evidence"u8)
        });
        version.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = (await version.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        var list = await client.GetFromJsonAsync<List<ContractDTO>>("/api/contracts", Json);
        list!.Single(c => c.Id == first.Id).DocumentCount.Should().Be(1);
        list!.Single(c => c.Id == second.Id).DocumentCount.Should().Be(1);
        await client.DeleteAsync($"/api/documents/{current.Id}");
        (await client.GetFromJsonAsync<ContractDTO>($"/api/contracts/{first.Id}", Json))!.DocumentCount.Should().Be(0);
        (await client.GetFromJsonAsync<ContractDTO>($"/api/contracts/{second.Id}", Json))!.DocumentCount.Should().Be(0);
    }
}
