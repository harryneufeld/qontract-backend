using Microsoft.AspNetCore.Http;
using Qontract.Application.Models;
using Qontract.Application.Services;

namespace Qontract.API.Services;

/// <summary>
/// Where the refresh token travels. Mobile clients keep it in the response body;
/// web clients get it as an HttpOnly cookie that scripts (and so XSS) cannot read.
/// </summary>
public static class SessionCookies
{
    public const string CookieName = "qontract_rt";
    public const string ClientHeader = "X-Qontract-Client";
    private const string CookiePath = "/api/auth";

    public static bool IsWeb(string? client) =>
        string.Equals(client?.Trim(), Qontract.Domain.Models.AuthSession.ClientWeb, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Moves the refresh token of a web session from the body into the cookie.
    /// Other clients, and responses without a new refresh token, pass through unchanged.
    /// </summary>
    public static AuthDTO Deliver(HttpResponse response, AuthDTO dto, bool web, AuthSessionOptions options)
    {
        if (!web || dto.RefreshToken is null) return dto;
        var expires = DateTime.UtcNow.AddDays(options.Web.IdleDays);
        if (dto.ExpiresAt < expires) expires = dto.ExpiresAt;
        response.Cookies.Append(CookieName, dto.RefreshToken, Options(expires));
        dto.RefreshToken = null;
        return dto;
    }

    public static void Clear(HttpResponse response) => response.Cookies.Delete(CookieName, Options(null));

    private static CookieOptions Options(DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Expires = expires is null ? null : new DateTimeOffset(DateTime.SpecifyKind(expires.Value, DateTimeKind.Utc))
    };
}
