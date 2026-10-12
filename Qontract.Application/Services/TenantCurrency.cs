using System.Text.Json;
using System.Text.RegularExpressions;

namespace Qontract.Application.Services;

public static class TenantCurrency
{
    public static string Normalize(string currency)
    {
        var code = currency.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(code, "^[A-Z]{3}$"))
            throw new InvalidOperationException("Enter a three-letter currency code.");
        return code;
    }

    public static string FromSettings(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings)) return "EUR";
        using var json = JsonDocument.Parse(settings);
        if (json.RootElement.ValueKind != JsonValueKind.Object
            || !json.RootElement.TryGetProperty("defaultCurrency", out var value)) return "EUR";
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Default currency must be a three-letter currency code.");
        return Normalize(value.GetString()!);
    }
}
