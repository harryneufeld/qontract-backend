using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Qontract.Application.Models;
using Qontract.API.Localization;
using Qontract.Infrastructure.Email;

namespace Qontract.Tests;

public class LocalizationTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;
    public LocalizationTests(QontractWebApplicationFactory factory) => _client = factory.CreateClient();
    private async Task Authenticate()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "admin@qontract.com", password = "password123" });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>())!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }
    private void Language(string? language)
    {
        _client.DefaultRequestHeaders.Remove("Accept-Language");
        if (language is not null) _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", language);
    }

    [Theory]
    [InlineData("de-DE,de;q=0.9,en;q=0.8", "de", "Titel ist erforderlich.")]
    [InlineData("en;q=0.9,de;q=0.2", "en", "Title is required.")]
    [InlineData("fr-FR", "en", "Title is required.")]
    [InlineData(null, "en", "Title is required.")]
    public async Task NegotiatesValidationLanguage_WithoutChangingFieldNames(string? requested, string resolved, string text)
    {
        Language(requested);
        await Authenticate();
        var response = await _client.PostAsJsonAsync("/api/contracts", new { title = "", status = "Active" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentLanguage.Should().Contain(resolved);
        response.Headers.Vary.Should().Contain("Accept-Language");
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["errors"]!["Title"]![0]!.GetValue<string>().Should().Be(text);
    }

    [Fact]
    public async Task GermanErrorsPreserveCodes_AndSuccessPreservesUserContentAndApiFormats()
    {
        Language("de"); await Authenticate();
        var missing = await _client.GetAsync("/api/contracts/2147483647");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = JsonNode.Parse(await missing.Content.ReadAsStringAsync())!;
        error["code"]!.GetValue<string>().Should().Be("not_found");
        error["message"]!.GetValue<string>().Should().Be("Der Eintrag wurde nicht gefunden.");
        var create = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "Subscription", description = "Name is required.", notes = "Original user text",
            type = "Subscription", billingPeriod = "Monthly", status = "Active",
            startDate = "2026-10-12", value = "1234.56", currency = "EUR"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var contract = JsonNode.Parse(await create.Content.ReadAsStringAsync())!;
        contract["title"]!.GetValue<string>().Should().Be("Subscription");
        contract["description"]!.GetValue<string>().Should().Be("Name is required.");
        contract["type"]!.GetValue<string>().Should().Be("Subscription");
        contract["billingPeriod"]!.GetValue<string>().Should().Be("Monthly");
        contract["startDate"]!.GetValue<string>().Should().Be("2026-10-12");
        contract["value"]!.GetValue<string>().Should().Be("1234.56");
        Language("en");
        var read = await _client.GetAsync($"/api/contracts/{contract["id"]}");
        var english = JsonNode.Parse(await read.Content.ReadAsStringAsync())!;
        english["title"]!.GetValue<string>().Should().Be("Subscription");
        english["value"]!.GetValue<string>().Should().Be("1234.56");
    }

    [Fact]
    public async Task FrameworkValidationAndLoginErrorsAreLocalized()
    {
        Language("de");
        var invalid = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "bad-email" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = JsonNode.Parse(await invalid.Content.ReadAsStringAsync())!;
        errors["errors"]!["Email"]![0]!.GetValue<string>().Should().Be("Bitte prüfe deine Eingaben und versuche es erneut.");
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "unknown@example.test", password = "invalid" });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = JsonNode.Parse(await login.Content.ReadAsStringAsync())!;
        body["error"]!.GetValue<string>().Should().Be("E-Mail-Adresse oder Passwort ungültig.");
    }

    [Theory]
    [InlineData("de", "Dein qontract-Passwort festlegen", "30 Minuten")]
    [InlineData("en", "Set your qontract password", "30 minutes")]
    [InlineData("fr", "Set your qontract password", "30 minutes")]
    public void PasswordEmailUsesExplicitLanguage_NotWorkerCulture(string language, string subject, string expiry)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Smtp:Host"] = "smtp.invalid", ["Smtp:Port"] = "587", ["Smtp:From"] = "sender@example.test" }).Build();
        var sender = new SmtpResetEmailSender(config);
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language == "de" ? "en" : "de");
            var link = new Uri("https://app.example.test/#/reset-password?token=FAKE&lang=" + language);
            var mail = sender.CreateMessage("recipient@example.test", link, language);
            mail.Subject.Should().Be(subject);
            mail.TextBody.Should().Contain(expiry).And.Contain(link.AbsoluteUri);
            mail.To.ToString().Should().Contain("recipient@example.test");
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("Name cannot exceed 100 characters.", "Name darf höchstens 100 Zeichen lang sein.")]
    [InlineData("New password must be at least 6 characters.", "Neues Passwort muss mindestens 6 Zeichen lang sein.")]
    [InlineData("Use a valid yyyy-MM-dd price effective date.", "Gib ein gültiges Datum für den Preisbeginn an.")]
    public void BusinessRulesKeepActionableLocalizedDetails(string message, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de"); UserMessages.Translate(message).Should().Be(expected); }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
