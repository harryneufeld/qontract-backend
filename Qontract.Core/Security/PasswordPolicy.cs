namespace Qontract.Core.Security;

/// <summary>
/// The single password policy for every owner-controlled credential flow:
/// registration, password reset and self-service change (issue #80).
/// Login deliberately enforces only the maximum, so accounts whose password
/// predates this policy can still sign in.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 10;
    public const int MaximumLength = 255;
}
