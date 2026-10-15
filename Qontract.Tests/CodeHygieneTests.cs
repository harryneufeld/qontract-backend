using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Qontract.Application.Models;

namespace Qontract.Tests;

/// <summary>
/// Regression tests for issue #39 (code hygiene):
///
/// 1. Uniform controller-side validation: every mutation endpoint now
///    validates through a DI-registered FluentValidation validator
///    (AddValidatorsFromAssembly) and rejects invalid payloads with 400
///    in one uniform shape: { errors: { field: [messages] } } — the shape
///    the Flutter app's _extractFieldError already consumes.
///
/// 2. Tenant deactivation no longer answers 404 after it has succeeded:
///    DeactivateTenantAsync used to re-read through GetTenantByIdAsync,
///    which filters IsActive, so the just-deactivated row vanished (the
///    DeactivateContractAsync workaround was missing on the tenant path).
///
/// 3. Address deactivation returns the deactivated AddressDTO instead of
///    a bare `true` (the endpoint promised AddressDTO), via the
///    honestly-named DeactivateAddressAsync service method (previously
///    DeleteAddressAsync, which never deletes).
/// </summary>
public class CodeHygieneTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly QontractWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public CodeHygieneTests(QontractWebApplicationFactory factory)
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

    /// <summary>
    /// Asserts the uniform 400 validation shape: { errors: { field: [messages] } }.
    /// </summary>
    private static async Task AssertValidationErrorAsync(HttpResponseMessage response, string property)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should().BeTrue($"400 body should carry an 'errors' map, got: {body}");
        errors.ValueKind.Should().Be(JsonValueKind.Object);
        errors.TryGetProperty(property, out var messages)
            .Should().BeTrue($"expected a validation error for '{property}', got: {body}");
        messages.ValueKind.Should().Be(JsonValueKind.Array);
        messages.GetArrayLength().Should().BeGreaterThan(0);
    }

    // -----------------------------------------------------------------
    // 1. Uniform controller-side validation (all six resource areas)
    // -----------------------------------------------------------------

    [Fact]
    public async Task CreateContract_TitleTooLong_Returns400_TitleError()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = new string('x', 201), // validator limit is 200
            status = "Active"
        });

        await AssertValidationErrorAsync(response, nameof(ContractCreateDTO.Title));
    }

    [Fact]
    public async Task CreateContractor_WithoutName_Returns400_NameError()
    {
        // The contractor endpoint previously returned a raw FluentValidation
        // error list; it must now use the same { errors: { field: [...] } }
        // map as every other endpoint.
        await AuthenticateAsAdminAsync();

        var response = await _client.PostAsJsonAsync("/api/contractors", new
        {
            type = "Individual"
        });

        await AssertValidationErrorAsync(response, nameof(ContractorCreateDTO.Name));
    }

    [Fact]
    public async Task CreateTenant_WithoutName_Returns400_NameError()
    {
        // Tenant validation used to run inside TenantService (new
        // TenantCreateValidator()); it must now run in the controller.
        await AuthenticateAsAdminAsync();

        var response = await _client.PostAsJsonAsync("/api/tenants", new
        {
            type = "Family"
        });

        await AssertValidationErrorAsync(response, nameof(TenantCreateDTO.Name));
    }

    [Fact]
    public async Task UpdateUser_WithContractorRole_Returns400_RoleError()
    {
        // User validation runs in the controller; the legacy Contractor role
        // is not assignable (issue #80).
        await AuthenticateAsAdminAsync();

        var response = await _client.PutAsJsonAsync("/api/users/1", new { role = "Contractor" });

        await AssertValidationErrorAsync(response, nameof(UserUpdateDTO.Role));
    }

    [Fact]
    public async Task CreateDocument_WithoutFileData_Returns400_FileDataError()
    {
        await AuthenticateAsAdminAsync();

        var contractResponse = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "Doc validation contract " + Guid.NewGuid().ToString("N"),
            status = "Active"
        });
        contractResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var contract = await contractResponse.Content.ReadFromJsonAsync<ContractDTO>(JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/documents/contract/{contract!.Id}/documents", new
            {
                fileName = "a.txt",
                fileType = "text/plain",
                contractId = contract.Id
            });

        await AssertValidationErrorAsync(response, nameof(DocumentCreateDTO.FileData));
    }

    [Fact]
    public async Task CreateAddress_WithoutStreet_Returns400_StreetError()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.PostAsJsonAsync("/api/addresses", new
        {
            entityType = "Tenant"
        });

        await AssertValidationErrorAsync(response, nameof(AddressCreateDTO.Street));
    }

    // -----------------------------------------------------------------
    // 2. Tenant deactivation must not 404 on the row it just deactivated
    // -----------------------------------------------------------------

    [Fact]
    public async Task DeactivateTenant_Returns200_DeactivatedTenant()
    {
        await AuthenticateAsAdminAsync();

        var create = await _client.PostAsJsonAsync(
            "/api/tenants", new { name = $"Deact {Guid.NewGuid():N}", type = "Family" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var tenant = await create.Content.ReadFromJsonAsync<TenantDTO>(JsonOptions);
        tenant.Should().NotBeNull();

        // Before the fix this answered 404: the service re-read the tenant
        // through GetTenantByIdAsync, which filters IsActive, so the row it
        // had just deactivated was invisible.
        var deact = await _client.PatchAsync($"/api/tenants/{tenant!.Id}/deactivate", new StringContent(""));
        deact.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await deact.Content.ReadFromJsonAsync<TenantDTO>(JsonOptions);
        result.Should().NotBeNull();
        result!.Id.Should().Be(tenant.Id);
        result.Name.Should().Be(tenant.Name);
        result.IsActive.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    // 3. Address deactivation returns the AddressDTO (not a bare bool)
    // -----------------------------------------------------------------

    /// <summary>
    /// 3. Address deactivation returns the deactivated AddressDTO (issue #32:
    ///    soft delete flips IsActive, the fields are preserved) instead of
    ///    a bare `true` (the old DeleteAddressAsync contract).
    /// </summary>
    [Fact]
    public async Task DeactivateAddress_Returns200_InactiveAddressDto()
    {
        await AuthenticateAsAdminAsync();

        var create = await _client.PostAsJsonAsync("/api/addresses", new
        {
            entityType = "Tenant",
            street = "Main St 1",
            city = "Berlin",
            zip = "10115",
            country = "Germany"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var address = await create.Content.ReadFromJsonAsync<AddressDTO>(JsonOptions);
        address.Should().NotBeNull();
        address!.Street.Should().Be("Main St 1");
        address.IsActive.Should().BeTrue();

        var deact = await _client.PatchAsync($"/api/addresses/{address.Id}/deactivate", new StringContent(""));
        deact.StatusCode.Should().Be(HttpStatusCode.OK);

        // The body must be the AddressDTO (the endpoint's declared type),
        // not the `true` the old DeleteAddressAsync returned.
        var body = await deact.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("id", out var id)
            .Should().BeTrue($"deactivate should return the AddressDTO, got: {body}");
        id.GetInt32().Should().Be(address.Id);
        // Soft delete (issue #32): the row keeps its fields but is inactive.
        doc.RootElement.GetProperty("street").GetString().Should().Be("Main St 1");
        doc.RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    // -----------------------------------------------------------------
    // Guard: user deactivation keeps working end-to-end
    // -----------------------------------------------------------------

    [Fact]
    public async Task DeactivateUser_Returns200_DeactivatedUser()
    {
        await AuthenticateAsAdminAsync();

        var user = await TestAccounts.CreateMemberAsync(_factory.Services, tenantId: 1);

        var deact = await _client.PatchAsync($"/api/users/{user.Id}/deactivate", new StringContent(""));
        deact.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await deact.Content.ReadFromJsonAsync<UserDTO>(JsonOptions);
        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
        result.IsActive.Should().BeFalse();
    }
}
