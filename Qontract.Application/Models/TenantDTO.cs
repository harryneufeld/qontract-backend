using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public class TenantDTO
{
    public string DefaultCurrency => Qontract.Application.Services.TenantCurrency.FromSettings(Settings);
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TenantType Type { get; set; }
    public string? Settings { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime ModifiedAt { get; set; }
}

// Validated by TenantCreateValidator / TenantUpdateValidator (issue #39),
// not DataAnnotations.
public class TenantCreateDTO
{
    public string Name { get; set; } = string.Empty;
    public TenantType Type { get; set; }
    public string? Settings { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TenantUpdateDTO
{
    public string? Name { get; set; }
    public TenantType? Type { get; set; }
    public string? Settings { get; set; }
    public bool? IsActive { get; set; }
}

public class ApplyTenantCurrencyDTO
{
    public bool ReplaceExisting { get; set; }
}