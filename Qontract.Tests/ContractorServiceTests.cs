using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Qontract.Application.FluentValidation;
using Qontract.Application.Models;
using Qontract.Application.Services;
using Qontract.Infrastructure.Data;
using Qontract.Domain.Models;

namespace Qontract.Tests;

public class ContractorServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ContractorService _service;

    public ContractorServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        // Arrange: one tenant with one contractor
        var tenant = new Tenant
        {
            Name = "Tenant A",
            Type = TenantType.Family,
            IsActive = true
        };
        var otherTenant = new Tenant
        {
            Name = "Tenant B",
            Type = TenantType.Family,
            IsActive = true
        };
        _context.Tenants.AddRange(tenant, otherTenant);
        _context.SaveChanges();

        _context.Contractors.Add(new Contractor
        {
            Name = "Alice Builder",
            Type = ContractorType.Individual,
            Email = "alice@builder.de",
            Phone = "+49 1234 56789",
            TenantId = tenant.Id,
            IsActive = true
        });
        _context.SaveChanges();

        _service = new ContractorService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private int TenantA() => _context.Tenants.First(t => t.Name == "Tenant A").Id;
    private int TenantB() => _context.Tenants.First(t => t.Name == "Tenant B").Id;

    [Fact]
    public async Task GetContractorsAsync_OnlyReturnsOwnTenant()
    {
        var result = await _service.GetContractorsAsync(TenantA());

        result.Should().HaveCount(1);
        result.Should().OnlyContain(c => c.TenantId == TenantA());
    }

    [Fact]
    public async Task GetContractorByIdAsync_ThrowsForOtherTenant()
    {
        var contractor = await _service.GetContractorsAsync(TenantA());
        var id = contractor.Single().Id;

        var act = () => _service.GetContractorByIdAsync(id, TenantB());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateContractorAsync_SetsTimestampsAndCreator()
    {
        var dto = new ContractorCreateDTO
        {
            Name = "New Roofer",
            Type = ContractorType.Company,
            Email = "roof@example.de",
            Phone = "+49 170 123456",
            TenantId = TenantA()
        };

        var created = await _service.CreateContractorAsync(dto, "admin@qontract.com");

        created.Id.Should().BePositive();
        created.CreatedBy.Should().Be("admin@qontract.com");
        created.ModifiedBy.Should().Be("admin@qontract.com");
        created.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        created.IsActive.Should().BeTrue();
        created.Email.Should().Be("roof@example.de");
        created.Phone.Should().Be("+49 170 123456");
    }

    [Fact]
    public async Task UpdateContractorAsync_OnlyUpdatesProvidedFields()
    {
        var existing = (await _service.GetContractorsAsync(TenantA())).Single();

        var updated = await _service.UpdateContractorAsync(existing.Id, TenantA(), new ContractorUpdateDTO
        {
            Name = "Renamed Roofer"
        }, "admin@qontract.com");

        updated.Name.Should().Be("Renamed Roofer");
        updated.Type.Should().Be(ContractorType.Individual); // untouched
        updated.Email.Should().Be("alice@builder.de"); // untouched
        updated.Phone.Should().Be("+49 1234 56789"); // untouched
        updated.ModifiedBy.Should().Be("admin@qontract.com");
    }

    [Fact]
    public async Task UpdateContractorAsync_UpdatesContactFields()
    {
        var existing = (await _service.GetContractorsAsync(TenantA())).Single();

        var updated = await _service.UpdateContractorAsync(existing.Id, TenantA(), new ContractorUpdateDTO
        {
            Email = "new@builder.de",
            Phone = "+49 30 987654"
        }, "admin@qontract.com");

        updated.Email.Should().Be("new@builder.de");
        updated.Phone.Should().Be("+49 30 987654");
    }

    [Fact]
    public async Task UpdateContractorAsync_ThrowsForOtherTenant()
    {
        var existing = (await _service.GetContractorsAsync(TenantA())).Single();

        var act = () => _service.UpdateContractorAsync(existing.Id, TenantB(), new ContractorUpdateDTO
        {
            Name = "Hijacked"
        }, "intruder@qontract.com");

        await act.Should().ThrowAsync<KeyNotFoundException>();

        // Verify nothing changed
        var after = await _service.GetContractorByIdAsync(existing.Id, TenantA());
        after.Name.Should().NotBe("Hijacked");
    }

    [Fact]
    public async Task DeactivateContractorAsync_ScopesToTenant()
    {
        var existing = (await _service.GetContractorsAsync(TenantA())).Single();

        var act = () => _service.DeactivateContractorAsync(existing.Id, TenantB(), "intruder@qontract.com");

        await act.Should().ThrowAsync<KeyNotFoundException>();

        // The contractor must still be active
        var after = await _service.GetContractorByIdAsync(existing.Id, TenantA());
        after.IsActive.Should().BeTrue();

        // Own-tenant deactivation works
        var deactivated = await _service.DeactivateContractorAsync(existing.Id, TenantA(), "admin@qontract.com");
        deactivated.IsActive.Should().BeFalse();
        deactivated.ModifiedBy.Should().Be("admin@qontract.com");
    }
}

public class ContractorValidatorTests
{
    private readonly ContractorCreateValidator _createValidator = new();
    private readonly ContractorUpdateValidator _updateValidator = new();

    [Fact]
    public void CreateValidator_ValidDto_ShouldBeValid()
    {
        var dto = new ContractorCreateDTO
        {
            Name = "Valid Contractor",
            Type = ContractorType.Company,
            Email = "contact@example.de",
            Phone = "+49 1234 56789"
        };

        _createValidator.Validate(dto).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateValidator_EmptyName_ShouldFail()
    {
        var dto = new ContractorCreateDTO
        {
            Name = "",
            Type = ContractorType.Individual
        };

        var result = _createValidator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ContractorCreateDTO.Name));
    }

    [Fact]
    public void CreateValidator_TooLongName_ShouldFail()
    {
        var dto = new ContractorCreateDTO
        {
            Name = new string('x', 101),
            Type = ContractorType.Individual
        };

        var result = _createValidator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ContractorCreateDTO.Name));
    }

    [Fact]
    public void CreateValidator_TooLongEmail_ShouldFail()
    {
        var dto = new ContractorCreateDTO
        {
            Name = "Valid",
            Type = ContractorType.Individual,
            Email = new string('c', 201)
        };

        _createValidator.Validate(dto).IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateValidator_InvalidEmailFormat_ShouldFail()
    {
        var dto = new ContractorCreateDTO
        {
            Name = "Valid",
            Type = ContractorType.Individual,
            Email = "not-an-email"
        };

        var result = _createValidator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ContractorCreateDTO.Email));
    }

    [Fact]
    public void CreateValidator_TooLongPhone_ShouldFail()
    {
        var dto = new ContractorCreateDTO
        {
            Name = "Valid",
            Type = ContractorType.Individual,
            Phone = new string('1', 51)
        };

        var result = _createValidator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ContractorCreateDTO.Phone));
    }

    [Fact]
    public void UpdateValidator_EmptyDto_ShouldBeValid()
    {
        // All update fields are optional; a no-op update must pass
        _updateValidator.Validate(new ContractorUpdateDTO()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateValidator_TooLongName_ShouldFail()
    {
        var dto = new ContractorUpdateDTO { Name = new string('x', 101) };

        _updateValidator.Validate(dto).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateValidator_TooLongEmail_ShouldFail()
    {
        var dto = new ContractorUpdateDTO { Email = new string('c', 201) };

        _updateValidator.Validate(dto).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateValidator_InvalidEmailFormat_ShouldFail()
    {
        var dto = new ContractorUpdateDTO { Email = "not-an-email" };

        _updateValidator.Validate(dto).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateValidator_TooLongPhone_ShouldFail()
    {
        var dto = new ContractorUpdateDTO { Phone = new string('1', 51) };

        _updateValidator.Validate(dto).IsValid.Should().BeFalse();
    }
}
