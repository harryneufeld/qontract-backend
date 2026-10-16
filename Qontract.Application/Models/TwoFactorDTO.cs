using System.ComponentModel.DataAnnotations;
namespace Qontract.Application.Models;
public record TwoFactorStatusDTO(bool Enabled, int RecoveryCodesRemaining);
public record TwoFactorSetupDTO(string Secret, string AuthenticatorUri);
public class TwoFactorProofDTO
{
    [Required, MaxLength(1024)] public string Password { get; set; } = "";
    [MaxLength(32)] public string Code { get; set; } = "";
    public bool UseRecoveryCode { get; set; }
}
