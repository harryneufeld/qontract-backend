using Microsoft.AspNetCore.DataProtection;
using Qontract.Core.Security;

namespace Qontract.API.Services;

public sealed class TwoFactorSecretProtector(IDataProtectionProvider provider) : ITwoFactorSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("qontract.totp.v1");
    public string Protect(string secret) => _protector.Protect(secret);
    public string Unprotect(string secret) => _protector.Unprotect(secret);
}
