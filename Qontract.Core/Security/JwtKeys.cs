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

    /// <summary>
    /// Prefix shared by the <c>Jwt__Key</c> placeholders in the committed
    /// <c>deploy/*.env.example</c> files. Those placeholders are long enough to
    /// pass the 32-byte length check, so an env file copied without editing
    /// would otherwise start with a publicly known signing key.
    /// </summary>
    public const string PlaceholderPrefix = "REPLACE_WITH";

    /// <summary>
    /// True when <paramref name="key"/> is publicly known — the development
    /// fallback or an unedited placeholder from the example env files — and
    /// therefore must not sign tokens outside Development.
    /// </summary>
    public static bool IsKnownInsecure(string key) =>
        string.Equals(key.Trim(), DevelopmentFallback, StringComparison.Ordinal)
        || key.TrimStart().StartsWith(PlaceholderPrefix, StringComparison.OrdinalIgnoreCase);
}
