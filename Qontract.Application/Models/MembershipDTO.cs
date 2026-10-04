using System.ComponentModel.DataAnnotations;
using Qontract.Domain.Models;

namespace Qontract.Application.Models;

public class MembershipDTO
{
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public TenantType Type { get; set; }
    public UserRole Role { get; set; }
}

public class SwitchTenantDTO
{
    // Zero selects the account's personal workspace; positive IDs require membership.
    [Range(0, int.MaxValue)] public int TenantId { get; set; }
}

public class RegisterDTO
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(100)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(255, MinimumLength = 10)] public string Password { get; set; } = string.Empty;
    [StringLength(100)] public string? TenantName { get; set; }
    [EnumDataType(typeof(TenantType))] public TenantType TenantType { get; set; } = TenantType.Family;
    [StringLength(64)] public string? InvitationCode { get; set; }
}

public class InvitationCreateDTO
{
    [EmailAddress, StringLength(100)] public string? Email { get; set; }
    [EnumDataType(typeof(UserRole))] public UserRole Role { get; set; } = UserRole.User;
    [Range(1, 30)] public int ValidDays { get; set; } = 7;
}

public class InvitationRedeemDTO
{
    [Required, StringLength(64, MinimumLength = 64)] public string Code { get; set; } = string.Empty;
}

public class InvitationDTO
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    public string? Email { get; set; }
    public UserRole Role { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public string? Code { get; set; }
}

public class MembershipUpdateDTO
{
    [EnumDataType(typeof(UserRole))] public UserRole? Role { get; set; }
    public bool? IsActive { get; set; }
}
