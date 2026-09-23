using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Qontract.Application.Models;

namespace Qontract.Tests;

/// <summary>
/// Issue #31: DB round-trip timestamps must not lose the UTC 'Z' suffix.
///
/// The create/update response serializes the in-memory entity (DateTime
/// Kind=Utc) so the timestamp carries a 'Z'. A subsequent GET reloads the
/// row from SQLite, where EF Core reads the value back with
/// Kind=Unspecified, and the default System.Text.Json converter would drop
/// the offset, so clients (Dart) would parse it as local time and display
/// dates shifted by the local UTC offset.
///
/// These tests assert on the *raw* JSON text so the exact serialized string
/// (including the 'Z') is what is compared between create and read-back.
/// </summary>
public class TimestampJsonConsistencyTests : IClassFixture<QontractWebApplicationFactory>
{
    private readonly HttpClient _client;

    public TimestampJsonConsistencyTests(QontractWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthDTO>();
        auth.Should().NotBeNull();
        return auth!.AccessToken;
    }

    private static string ExtractRawField(string rawJson, string field)
    {
        using var doc = JsonDocument.Parse(rawJson);
        if (!doc.RootElement.TryGetProperty(field, out var prop)
            || prop.ValueKind != JsonValueKind.String)
        {
            throw new KeyNotFoundException($"String field '{field}' not found in JSON: {rawJson}");
        }
        return prop.GetString()!;
    }

    [Fact]
    public async Task ContractTimestamps_ShouldBeIdenticalBetweenCreateAndGet_WithZ()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "UTC JSON Consistency",
            status = "Active"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createRaw = await createResponse.Content.ReadAsStringAsync();
        var createId = JsonDocument.Parse(createRaw).RootElement.GetProperty("id").GetInt32();

        var getResponse = await _client.GetAsync($"/api/contracts/{createId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getRaw = await getResponse.Content.ReadAsStringAsync();

        var createCreatedAt = ExtractRawField(createRaw, "createdAt");
        var getCreatedAt = ExtractRawField(getRaw, "createdAt");
        var createModifiedAt = ExtractRawField(createRaw, "modifiedAt");
        var getModifiedAt = ExtractRawField(getRaw, "modifiedAt");

        // Both responses must carry an explicit UTC designator.
        createCreatedAt.Should().EndWith("Z", "create response createdAt");
        getCreatedAt.Should().EndWith("Z", "GET response createdAt (issue #31)");
        createModifiedAt.Should().EndWith("Z", "create response modifiedAt");
        getModifiedAt.Should().EndWith("Z", "GET response modifiedAt (issue #31)");

        // The stored instant is byte-stable in SQLite, so the exact serialized
        // strings must match between the create response and the GET.
        getCreatedAt.Should().Be(createCreatedAt);
        getModifiedAt.Should().Be(createModifiedAt);
    }

    [Fact]
    public async Task DocumentTimestamps_ShouldCarryZSuffixAfterPersistence()
    {
        var token = await GetTokenAsync("admin@qontract.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var contractResponse = await _client.PostAsJsonAsync("/api/contracts", new
        {
            title = "Doc Timestamp Host",
            status = "Active"
        });
        contractResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var contractId = JsonDocument.Parse(await contractResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("utc stamp test payload"));
        var uploadResponse = await _client.PostAsJsonAsync($"/api/documents/contract/{contractId}/documents", new
        {
            fileName = "utc-stamp.txt",
            fileType = "text/plain",
            fileData = base64,
            contractId
        });
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadRaw = await uploadResponse.Content.ReadAsStringAsync();
        var docId = JsonDocument.Parse(uploadRaw).RootElement.GetProperty("id").GetInt32();

        var getResponse = await _client.GetAsync($"/api/documents/{docId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getRaw = await getResponse.Content.ReadAsStringAsync();

        foreach (var field in new[] { "uploadedAt", "createdAt", "modifiedAt" })
        {
            var raw = ExtractRawField(getRaw, field);
            raw.Should().EndWith("Z", $"GET {field} for documents must carry the UTC designator (issue #31)");
        }
    }
}
