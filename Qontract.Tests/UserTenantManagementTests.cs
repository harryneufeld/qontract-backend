using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Qontract.Application.FluentValidation;
using Qontract.Application.Models;
using Qontract.Application.Services;
using Qontract.Domain.Models;

namespace Qontract.Tests;

/// <summary>
/// Issue #40: three pre-existing bugs in the user/tenant management flow.
///
/// 1. <see cref="AuthService.HashPasswordAsync"/> 500'd on any plain password
///    whose first ':'-segment is not the literal string "salt", because it
///    unconditionally ran <c>Convert.FromBase64String</c> on it. A plain
///    password such as "password123" is not valid base64 -> FormatException.
///    Now a fresh salt is generated unless the input is a genuine
///    two-segment "salt:hash" pair.
///
/// 2. <c>TenantCreateValidator</c>/<c>TenantUpdateValidator</c> used
///    <c>NotEmpty()</c> on the <see cref="TenantType"/> enum, which treats the
///    enum default (<c>Family</c> == 0) as empty, so <c>type:"Family"</c>
///    always failed. Now <c>IsInEnum()</c>.
///
/// 3. <c>UserCreateValidator</c> had the same quirk on <see cref="UserRole"/>,
///    so <c>role:"TenantAdmin"</c> (== 0) always failed. Now <c>IsInEnum()</c>.
///
/// The earlier workaround tests (RoleAuthorizationTests, UserDtoSecretLeakTests)
/// deliberately used a base64 password + <c>role:"User"</c> + <c>type:"Company"</c>
/// to dodge these bugs; this class asserts the correct values now work.
/// </summary>
public class UserTenantManagementTests
{
    // -----------------------------------------------------------------
    // Unit tests: HashPasswordAsync no longer throws on plain passwords.
    // (AuthService is constructed without a DbContext / configuration
    // because HashPasswordAsync only needs a logger.)
    // -----------------------------------------------------------------

    private static AuthService BuildAuthService() =>
        new AuthService(null!, new ConfigurationBuilder().Build(), NullLogger<AuthService>.Instance);

    private static (string Salt, string Hash) SplitHash(string hash)
    {
        var parts = hash.Split(':');
        parts.Length.Should().Be(4, "new passwords use a versioned PBKDF2 format");
        parts[0].Should().Be("pbkdf2-sha512");
        return (parts[2], parts[3]);
    }

    [Fact]
    public async Task HashPassword_PlainNonBase64Password_DoesNotThrow_AndReturnsSaltedHash()
    {
        // "password123" is NOT valid base64 (length 11, not a multiple of 4) —
        // before the fix this threw FormatException and surfaced as an HTTP 500.
        var service = BuildAuthService();

        var hash = await service.HashPasswordAsync("password123");

        var (salt, hashPart) = SplitHash(hash);
        Convert.FromBase64String(salt).Length.Should().Be(16, "the generated salt is 16 bytes");
        Convert.FromBase64String(hashPart).Length.Should().Be(32);
        Qontract.Core.Security.Passwords.Verify("password123", hash).Should().BeTrue();
        Qontract.Core.Security.Passwords.Verify("wrong-password", hash).Should().BeFalse();
    }

    [Theory]
    // Plain passwords that are not a clean two-segment "salt:hash" pair must
    // all be hashed with a fresh salt instead of failing to decode.
    [InlineData("password123")]
    [InlineData("")]
    [InlineData("a:b")]                 // colon, but segments are not base64
    [InlineData("pässword 123!!")]     // unicode / special characters
    [InlineData("cGFzc3dvcmQxMjM=")]    // valid base64 but no colon -> plain
    public async Task HashPassword_DiversePlainPasswords_AllSucceed(string password)
    {
        var service = BuildAuthService();

        var hash = await service.HashPasswordAsync(password);

        SplitHash(hash).Salt.Should().NotBeNullOrEmpty();
        SplitHash(hash).Hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HashPassword_TwoDistinctPasswords_ProduceDifferentHashes()
    {
        // Guard against a degenerate fix that ignored the password.
        var service = BuildAuthService();

        var a = await service.HashPasswordAsync("password123");
        var b = await service.HashPasswordAsync("password456");

        a.Should().NotBe(b);
    }

    [Fact]
    public void TenantValidator_AcceptsFamilyAndCompany()
    {
        var validator = new TenantCreateValidator();

        var family = validator.Validate(new TenantCreateDTO { Name = "T", Type = TenantType.Family });
        family.IsValid.Should().BeTrue(
            $"Family (0) must be accepted, got: {string.Join("; ", family.Errors.Select(e => e.ErrorMessage))}");

        var company = validator.Validate(new TenantCreateDTO { Name = "T", Type = TenantType.Company });
        company.IsValid.Should().BeTrue();
    }

    [Fact]
    public void TenantValidator_RejectsUnknownType()
    {
        var validator = new TenantCreateValidator();

        var result = validator.Validate(new TenantCreateDTO { Name = "T", Type = (TenantType)99 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(TenantCreateDTO.Type));
    }

    [Fact]
    public void UserValidator_AcceptsTenantAdmin()
    {
        var validator = new UserCreateValidator();

        var result = validator.Validate(new UserCreateDTO
        {
            Name = "A",
            Email = "a@example.com",
            Password = "x",
            Role = UserRole.TenantAdmin
        });

        result.IsValid.Should().BeTrue(
            $"TenantAdmin (0) must be accepted, got: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
    }

    [Fact]
    public void UserValidator_RejectsUnknownRole()
    {
        var validator = new UserCreateValidator();

        var result = validator.Validate(new UserCreateDTO
        {
            Name = "A",
            Email = "a@example.com",
            Password = "x",
            Role = (UserRole)99
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(UserCreateDTO.Role));
    }

    // -----------------------------------------------------------------
    // Integration tests: the full create/login/tenant flows over HTTP.
    // -----------------------------------------------------------------

    public sealed class HttpRegressionTests : IClassFixture<QontractWebApplicationFactory>
    {
        private readonly HttpClient _client;
        private readonly QontractWebApplicationFactory _factory;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public HttpRegressionTests(QontractWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private async Task AuthenticateAsAdminAsync()
        {
            var login = await _client.PostAsJsonAsync(
                "/api/auth/login", new { email = "admin@qontract.com", password = "password123" });
            login.StatusCode.Should().Be(HttpStatusCode.OK);
            var auth = await login.Content.ReadFromJsonAsync<AuthDTO>();
            auth.Should().NotBeNull();
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        }

        [Fact]
        public async Task CreateUser_PlainPassword_And_TenantAdminRole_Succeeds()
        {
            // Bug 1 (plain password 500) + Bug 3 (TenantAdmin role rejected),
            // exercised together the way a real client sends them.
            await AuthenticateAsAdminAsync();

            const string plainPassword = "Sup3r-Secret!"; // not base64, has special chars
            var email = $"newadmin{Guid.NewGuid():N}@qontract.com";

            var create = await _client.PostAsJsonAsync("/api/users", new
            {
                name = "New Tenant Admin",
                email,
                password = plainPassword,
                role = "TenantAdmin",
                tenantId = 1
            });

            create.StatusCode.Should().Be(HttpStatusCode.Created);

            var dto = await create.Content.ReadFromJsonAsync<UserDTO>(JsonOptions);
            dto!.Role.Should().Be(UserRole.TenantAdmin);
            dto.Id.Should().BePositive();

            // And the new user must actually be able to log in with the very
            // plain password that was sent — proves VerifyPassword round-trips.
            _client.DefaultRequestHeaders.Authorization = null;
            var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = plainPassword });
            login.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task CreateTenant_FamilyType_Succeeds()
        {
            // Bug 2: type "Family" (TenantType == 0) was rejected.
            await AuthenticateAsAdminAsync();

            var create = await _client.PostAsJsonAsync(
                "/api/tenants", new { name = $"Family Tenant {Guid.NewGuid():N}", type = "Family" });

            create.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await create.Content.ReadFromJsonAsync<TenantDTO>(JsonOptions);
            dto!.Type.Should().Be(TenantType.Family);
        }

        [Fact]
        public async Task CreateTenant_CompanyType_StillSucceeds()
        {
            // Regression guard: Company (1) already worked and must keep working.
            await AuthenticateAsAdminAsync();

            var create = await _client.PostAsJsonAsync(
                "/api/tenants", new { name = $"Company Tenant {Guid.NewGuid():N}", type = "Company" });

            create.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await create.Content.ReadFromJsonAsync<TenantDTO>(JsonOptions);
            dto!.Type.Should().Be(TenantType.Company);
        }

        [Fact]
        public async Task CreateUser_UnknownRole_IsRejected()
        {
            // IsInEnum() must still reject genuinely invalid enum strings.
            await AuthenticateAsAdminAsync();

            var create = await _client.PostAsJsonAsync("/api/users", new
            {
                name = "Bad Role",
                email = $"badrole{Guid.NewGuid():N}@qontract.com",
                password = "whatever123",
                role = "DefinitelyNotARole",
                tenantId = 1
            });

            create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task CreateTenant_UnknownType_IsRejected()
        {
            await AuthenticateAsAdminAsync();

            var create = await _client.PostAsJsonAsync(
                "/api/tenants", new { name = "Bad Type", type = "NotAType" });

            create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
