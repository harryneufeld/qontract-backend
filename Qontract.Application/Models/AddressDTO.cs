using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public class AddressDTO
{
    public int Id { get; set; }
    public EntityType EntityType { get; set; }
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? City { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public bool IsActive { get; set; }
}

// Validated by AddressCreateValidator (issue #39), not DataAnnotations.
public class AddressCreateDTO
{
    public EntityType EntityType { get; set; }

    /// <summary>
    /// Owning contractor for <c>EntityType.Contractor</c> addresses (issue
    /// #32). Must belong to the caller's tenant — the service verifies and
    /// 404s otherwise.
    /// </summary>
    public int? ContractorId { get; set; }

    /// <summary>
    /// Owning contract for <c>EntityType.Contract</c> addresses (issue #32).
    /// Must belong to the caller's tenant — the service verifies and 404s
    /// otherwise.
    /// </summary>
    public int? ContractId { get; set; }

    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? City { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
}

public class AddressUpdateDTO : AddressCreateDTO { }
