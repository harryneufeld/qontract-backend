namespace Qontract.Core.Security;

/// <summary>
/// Shared JWT signing-key constants.
/// </summary>
public static class JwtKeys
{
    /// <summary>
    /// Development-only fallback signing key, used when <c>Jwt:Key</c> is not
    /// configured in the Development environment (issue #36).
    ///
    /// This single constant is referenced by BOTH the token-validation pipeline
    /// (<c>Qontract.API/Program.cs</c>) and the token-issuing side
    /// (<c>Qontract.Application/Services/AuthService.cs</c>). Previously the
    /// literal was duplicated in the two files, so a change in one place
    /// silently stopped the other from validating/issuing matching tokens.
    ///
    /// This key is NOT secret-safe and must never be used in production;
    /// non-Development environments fail fast when <c>Jwt:Key</c> is missing.
    /// </summary>
    public const string DevelopmentFallback =
        "qontract-development-fallback-key-DO-NOT-USE-IN-PROD";
}
