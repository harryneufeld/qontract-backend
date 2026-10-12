namespace Qontract.Application.Localization;

public static class SupportedLanguage
{
    public static string Normalize(string? language) =>
        language?.Split('-')[0].Equals("de", StringComparison.OrdinalIgnoreCase) == true ? "de" : "en";
}
