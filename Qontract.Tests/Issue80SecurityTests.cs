using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Qontract.API.Controllers;
using Qontract.API.Middleware;
using Qontract.Application.Models;
using Qontract.Domain.Models;
using Qontract.Infrastructure.Data;

namespace Qontract.Tests;

/// <summary>
/// Regression tests for the issue #80 security review:
/// 1. Administrators cannot replace member credentials or create accounts
///    with a password they know (account takeover into personal content).
/// 2. The legacy Contractor role cannot be assigned (content denial is in
///    RoleAuthorizationTests).
/// 3. Legacy SHA-256 hashes still verify, including the historic truncated
///    encoding of non-ASCII passwords, and are upgraded to PBKDF2 on login.
/// 4. One password policy for owner-controlled flows; login keeps old passwords working.
/// 5. Unknown accounts and wrong passwords fail identically.
/// 6. Framework InvalidOperationExceptions do not leak their messages.
/// 7. A client-supplied contractor account link is ignored.
/// 8. Security headers and safe download content types.
/// </summary>
public class Issue80SecurityTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly QontractWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public Issue80SecurityTests(QontractWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> LoginResponse(string email, string password) =>
        await _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task<AuthDTO> LoginAsAdmin()
    {
        var response = await LoginResponse("admin@qontract.com", "password123");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>())!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }

    private async Task<User> ReadUser(int id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    // ---------------------------------------------------------------- 1

    [Fact]
    public async Task Admin_CannotReplaceMemberPasswordOrEmail()
    {
        var admin = await LoginAsAdmin();
        var member = await TestAccounts.CreateMemberAsync(_factory.Services, admin.User!.TenantId,
            password: "member-own-password");
        var before = await ReadUser(member.Id);

        var update = await _client.PutAsJsonAsync($"/api/users/{member.Id}", new
        {
            name = "Taken over", email = "attacker@example.test", password = "admin-chosen-password", role = "User"
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK, "role/activity updates remain allowed; account fields are ignored");

        var after = await ReadUser(member.Id);
        after.Email.Should().Be(member.Email);
        after.Name.Should().Be(before.Name);
        after.PasswordHash.Should().Be(before.PasswordHash);
        after.AuthVersion.Should().Be(before.AuthVersion, "the member's sessions must not be disturbed either");

        _client.DefaultRequestHeaders.Authorization = null;
        (await LoginResponse(member.Email, "admin-chosen-password")).IsSuccessStatusCode.Should().BeFalse();
        (await LoginResponse("attacker@example.test", "admin-chosen-password")).IsSuccessStatusCode.Should().BeFalse();
        (await LoginResponse(member.Email, "member-own-password")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_CannotCreateAccountsWithKnownPassword()
    {
        await LoginAsAdmin();
        var email = $"admin-made-{Guid.NewGuid():N}@qontract.com";

        var create = await _client.PostAsJsonAsync("/api/users", new
        {
            name = "Admin made", email, password = "admin-chosen-password", role = "User"
        });

        create.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(u => u.Email == email))
            .Should().BeFalse();
    }

    // ---------------------------------------------------------------- 2 + 6 (Qontract-thrown rule passes through)

    [Fact]
    public async Task Invitation_WithContractorRole_IsRejectedWithBusinessMessage()
    {
        var admin = await LoginAsAdmin();

        var invite = await _client.PostAsJsonAsync($"/api/tenants/{admin.User!.TenantId}/invitations", new { role = "Contractor" });

        invite.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invite.Content.ReadAsStringAsync()).Should().Contain("Invalid role.");
    }

    // ---------------------------------------------------------------- 3

    private static string LegacyHash(string password, bool truncated)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var encoded = Encoding.UTF8.GetBytes(password);
        var length = truncated ? password.Length : encoded.Length;
        var hash = SHA256.HashData(salt.Concat(encoded.Take(length)).ToArray());
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    [Theory]
    [InlineData("legacy-password", true)]
    [InlineData("Pässwört-Geheim", true)]   // historic layout drops trailing UTF-8 bytes
    [InlineData("Pässwört-Geheim", false)]  // full encoding
    public async Task LegacyHash_VerifiesAndIsUpgradedOnLogin(string password, bool truncated)
    {
        var member = await TestAccounts.CreateMemberAsync(_factory.Services, tenantId: 1);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Users.Where(u => u.Id == member.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, LegacyHash(password, truncated)));
        }

        var authVersion = (await ReadUser(member.Id)).AuthVersion;
        (await LoginResponse(member.Email, "wrong-" + password)).IsSuccessStatusCode.Should().BeFalse();
        (await LoginResponse(member.Email, password)).StatusCode.Should().Be(HttpStatusCode.OK);

        var upgraded = await ReadUser(member.Id);
        upgraded.PasswordHash.Should().StartWith("pbkdf2-sha512:");
        upgraded.AuthVersion.Should().Be(authVersion, "an upgrade is not a credential change");
        (await LoginResponse(member.Email, password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- 4

    [Fact]
    public async Task OwnerFlows_EnforceTheSharedMinimumLength()
    {
        await LoginAsAdmin();
        var change = await _client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "password123", newPassword = "nine-char" });
        change.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        _client.DefaultRequestHeaders.Authorization = null;
        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { token = new string('a', 64), newPassword = "nine-char" });
        reset.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var register = await _client.PostAsJsonAsync("/api/auth/register",
            new { name = "Short", email = $"short-{Guid.NewGuid():N}@qontract.com", password = "nine-char" });
        register.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_StillAcceptsPasswordsSetBeforeThePolicy()
    {
        var member = await TestAccounts.CreateMemberAsync(_factory.Services, tenantId: 1, password: "short");

        (await LoginResponse(member.Email, "short")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- 5

    [Fact]
    public async Task Login_UnknownAccountAndWrongPassword_FailIdentically()
    {
        var unknown = await LoginResponse($"nobody-{Guid.NewGuid():N}@qontract.com", "whatever-password");
        var wrong = await LoginResponse("admin@qontract.com", "whatever-password");

        unknown.StatusCode.Should().Be(wrong.StatusCode);
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await wrong.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- 6

    [Fact]
    public void FrameworkInvalidOperationException_IsNotExposed()
    {
        var framework = Record.Exception(() => Enumerable.Empty<int>().First());
        framework.Should().BeOfType<InvalidOperationException>();

        var (status, code, message) = ExceptionHandlingMiddleware.MapException(framework!);

        status.Should().Be(500);
        code.Should().Be("internal_error");
        message.Should().NotContain(framework!.Message);
    }

    // ---------------------------------------------------------------- 7

    [Fact]
    public async Task ContractorCreate_IgnoresClientSuppliedUserId()
    {
        await LoginAsAdmin();

        var create = await _client.PostAsJsonAsync("/api/contractors", new { name = "Supplier", type = "Company", userId = 1 });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<ContractorDTO>();

        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Contractors
            .AsNoTracking().SingleAsync(c => c.Id == created!.Id);
        stored.UserId.Should().BeNull();
    }

    // ---------------------------------------------------------------- 8

    [Theory]
    [InlineData("/api/auth/me", HttpStatusCode.OK)]
    [InlineData("/api/contracts", HttpStatusCode.OK)]
    [InlineData("/api/contracts/987654", HttpStatusCode.NotFound)]
    public async Task ApiResponses_CarryNoStoreAndNoSniff(string route, HttpStatusCode expected)
    {
        await LoginAsAdmin();

        var response = await _client.GetAsync(route);

        response.StatusCode.Should().Be(expected);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
    }

    [Fact]
    public async Task TokenResponse_IsNotCacheable()
    {
        var response = await LoginResponse("admin@qontract.com", "password123");

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Theory]
    [InlineData("application/pdf", "application/pdf")]
    [InlineData("Image/PNG", "image/png")]
    [InlineData("text/plain; charset=utf-8", "text/plain")]
    [InlineData("text/html", "application/octet-stream")]
    [InlineData("image/svg+xml", "application/octet-stream")]
    [InlineData("not a type", "application/octet-stream")]
    [InlineData(null, "application/octet-stream")]
    public void DownloadContentType_OnlyServesKnownTypes(string? stored, string served) =>
        DocumentsController.DownloadContentType(stored).Should().Be(served);

    [Fact]
    public async Task Download_OfUploadedHtml_IsServedAsBinaryAttachment()
    {
        await LoginAsAdmin();
        var contract = await _client.PostAsJsonAsync("/api/contracts", new { title = "Header check", status = "Active" });
        var contractId = (await contract.Content.ReadFromJsonAsync<IdJson>())!.Id;
        var upload = await _client.PostAsJsonAsync($"/api/documents/contract/{contractId}/documents", new
        {
            fileName = "page.html", fileType = "text/html", contractId,
            fileData = Convert.ToBase64String(Encoding.UTF8.GetBytes("<script>alert(1)</script>"))
        });
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var documentId = (await upload.Content.ReadFromJsonAsync<IdJson>())!.Id;

        var download = await _client.GetAsync($"/api/documents/{documentId}/download");

        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
    }

    private sealed record IdJson(int Id);
}
