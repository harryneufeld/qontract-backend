namespace Qontract.API;

/// <summary>
/// Role names used in [Authorize(Roles = ...)] attributes. The values must
/// match what the JWT token carries in its role claim: <see cref="AuthService"
/// /> signs <c>ClaimTypes.Role</c> with <c>UserRole.ToString()</c> from
/// Qontract.Domain (issue #24). Keep the two in sync — a typo here silently
/// turns every request into a 403.
/// </summary>
public static class AppRoles
{
    /// <summary>Can manage users, tenants and invite members.</summary>
    public const string TenantAdmin = nameof(TenantAdmin);

    /// <summary>Regular tenant member.</summary>
    public const string User = nameof(User);

    /// <summary>
    /// Legacy role kept only so stored memberships still deserialize. It
    /// cannot be assigned and grants no content or management access: only
    /// administrators and regular members use the app (issue #80).
    /// </summary>
    public const string Contractor = nameof(Contractor);
}
