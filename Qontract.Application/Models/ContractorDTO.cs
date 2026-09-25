using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public class ContractorDTO
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ContractorType Type { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int TenantId { get; set; }
    public int? UserId { get; set; }
    public bool IsActive { get; set; }
    /// <summary>
    /// Total number of the tenant's contracts referencing this contractor
    /// (app#33). Computed server-side because the app's contract list is
    /// paginated, so the client cannot count from the loaded slice.
    /// </summary>
    public int ContractCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}

// Validated by ContractorCreateValidator / ContractorUpdateValidator
// (issue #39), not DataAnnotations.
public class ContractorCreateDTO
{
    public string Name { get; set; } = string.Empty;
    public ContractorType Type { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    public int TenantId { get; set; }

    public int? UserId { get; set; }
}

public class ContractorUpdateDTO
{
    public string? Name { get; set; }
    public ContractorType? Type { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool? IsActive { get; set; }
}
