using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qontract.Application.Models;
using Qontract.Application.Services;
using Qontract.Infrastructure.Data;
using Xunit;

namespace Qontract.Tests;

[Collection("QontractApiStorage")]
public class InvoiceAndSharedDocumentTests(QontractWebApplicationFactory factory) : IClassFixture<QontractWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    private async Task<HttpClient> Client(string email = "admin@qontract.com")
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        var auth = (await response.Content.ReadFromJsonAsync<AuthDTO>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }
    private static async Task<int> Contract(HttpClient c)
    {
        var response = await c.PostAsJsonAsync("/api/contracts", new { title = "Invoice-" + Guid.NewGuid(), status = "Active" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ContractDTO>(Json))!.Id;
    }
    private static async Task<DocumentDTO> Upload(HttpClient c, int contract, string content)
    {
        var response = await c.PostAsJsonAsync($"/api/documents/contract/{contract}/documents", new { fileName = "same.txt", fileType = "text/plain", fileData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content)) });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
    }
    private static InvoiceInput Input(int? contract = null, int? document = null) => new()
    { InvoiceDate = "2026-10-01", DueDate = "2026-10-31", Amount = "-1234567890,123456789", InvoiceNumber = "Credit", ContractId = contract, DocumentId = document };

    [Fact]
    public async Task SignedDecimalUnknownCurrencyAndArchiveRestoreRoundTrip()
    {
        using var client = await Client();
        var response = await client.PostAsJsonAsync("/api/invoices", Input());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDTO>(Json))!;
        invoice.Amount.Should().Be("-1234567890.123456789"); invoice.Currency.Should().BeNull();
        var changed = Input(); changed.Currency = "eur";
        (await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", changed)).StatusCode.Should().Be(HttpStatusCode.OK);
        var fresh = (await client.GetFromJsonAsync<InvoiceDTO>($"/api/invoices/{invoice.Id}", Json))!;
        fresh.Currency.Should().Be("EUR"); fresh.Amount.Should().Be(invoice.Amount);
        (await client.DeleteAsync($"/api/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<List<InvoiceDTO>>("/api/invoices", Json))!.Should().NotContain(i => i.Id == invoice.Id);
        (await client.GetFromJsonAsync<List<InvoiceDTO>>("/api/invoices?deleted=true", Json))!.Should().Contain(i => i.Id == invoice.Id);
        (await client.PostAsync($"/api/invoices/{invoice.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForeignTenantCannotReadChangeOrAttachInvoicesOrFiles()
    {
        using var client = await Client(); using var foreign = await Client("admin2@qontract.com");
        var contract = await Contract(client); var doc = await Upload(client, contract, "Evidence");
        var response = await client.PostAsJsonAsync("/api/invoices", Input(contract, doc.Id));
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDTO>(Json))!;
        (await foreign.GetAsync($"/api/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.PutAsJsonAsync($"/api/invoices/{invoice.Id}", Input())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.DeleteAsync($"/api/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.PostAsync($"/api/invoices/{invoice.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.PostAsJsonAsync("/api/invoices", Input(null, doc.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.GetAsync($"/api/invoices?contractId={contract}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.GetFromJsonAsync<List<InvoiceDTO>>("/api/invoices", Json))!.Should().NotContain(i => i.Id == invoice.Id);
        var foreignContract = await Contract(foreign);
        (await client.PostAsync($"/api/documents/{doc.Id}/contracts/{foreignContract}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.PostAsync($"/api/documents/{doc.Id}/contracts/{foreignContract}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShareReuploadUnlinkKeepExactEvidenceAndDoNotCopyOrOverwriteBytes()
    {
        using var client = await Client();
        var a = await Contract(client); var b = await Contract(client);
        var first = await Upload(client, a, "Original"); var second = await Upload(client, a, "Different same-name file");
        System.Text.Encoding.UTF8.GetString(await client.GetByteArrayAsync($"/api/documents/{first.Id}/download")).Should().Be("Original");
        (await client.PostAsync($"/api/documents/{first.Id}/contracts/{b}", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/documents/{first.Id}/contracts/{b}", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var shared = (await client.GetFromJsonAsync<List<DocumentDTO>>($"/api/documents/contract/{b}/documents", Json))!;
        shared.Should().ContainSingle(); shared[0].Id.Should().Be(first.Id); shared[0].ContractIds.Should().BeEquivalentTo(new[] { a, b });
        var response = await client.PostAsJsonAsync("/api/invoices", Input(b, first.Id));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDTO>(Json))!;
        (await client.DeleteAsync($"/api/documents/{first.Id}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.DeleteAsync($"/api/documents/{first.Id}/contracts/{b}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var reupload = await client.PostAsJsonAsync($"/api/documents/{first.Id}/reupload", new { fileData = Convert.ToBase64String("Revision"u8), fileName = "same.txt", fileType = "text/plain" });
        reupload.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = (await reupload.Content.ReadFromJsonAsync<DocumentDTO>(Json))!;
        current.ContractIds.Should().BeEquivalentTo(new[] { a, b });
        (await client.GetFromJsonAsync<List<DocumentDTO>>($"/api/documents/contract/{b}/documents", Json))!.Single().Id.Should().Be(current.Id);
        (await client.GetFromJsonAsync<InvoiceDTO>($"/api/invoices/{invoice.Id}", Json))!.DocumentId.Should().Be(first.Id);
        System.Text.Encoding.UTF8.GetString(await client.GetByteArrayAsync($"/api/documents/{first.Id}/download")).Should().Be("Original");
        (await client.DeleteAsync($"/api/documents/{current.Id}/contracts/{a}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.DeleteAsync($"/api/documents/{current.Id}/contracts/{b}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<List<DocumentDTO>>($"/api/documents/contract/{b}/documents", Json))!.Should().BeEmpty();
        (await client.GetAsync($"/api/documents/{second.Id}/download")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ArchivedInvoiceEvidenceSurvivesPurge()
    {
        using var client = await Client();
        var contract = await Contract(client); var doc = await Upload(client, contract, "Retained");
        var response = await client.PostAsJsonAsync("/api/invoices", Input(contract, doc.Id));
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDTO>(Json))!;
        await client.DeleteAsync($"/api/invoices/{invoice.Id}");
        (await client.DeleteAsync($"/api/documents/{doc.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = (await db.Documents.FindAsync(doc.Id))!;
        row.DeletedAt = DateTime.UtcNow.AddYears(-1); await db.SaveChangesAsync();
        // Simulate the old same-name path collision: purging another metadata
        // row must not remove bytes still referenced by retained evidence.
        var duplicate = new Qontract.Domain.Models.Document
        {
            ContractId = contract, FileName = row.FileName, FileType = row.FileType,
            FilePath = row.FilePath, FileSize = row.FileSize, UploadedBy = "legacy",
            Version = 1, IsDeleted = true, DeletedAt = DateTime.UtcNow.AddYears(-1)
        };
        db.Documents.Add(duplicate); await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<DocumentPurgeService>().PurgeExpiredAsync();
        (await db.Documents.AnyAsync(d => d.Id == duplicate.Id)).Should().BeFalse();
        (await db.Documents.AnyAsync(d => d.Id == doc.Id)).Should().BeTrue();
        var trash = (await client.GetFromJsonAsync<List<DocumentDTO>>("/api/documents/trash", Json))!;
        trash.Single(d => d.Id == doc.Id).RetainedForInvoice.Should().BeTrue();
        trash.Single(d => d.Id == doc.Id).ExpiresAt.Should().BeNull();
        (await client.PostAsync($"/api/invoices/{invoice.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsync($"/api/documents/{doc.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/invoices/{invoice.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        System.Text.Encoding.UTF8.GetString(await client.GetByteArrayAsync($"/api/documents/{doc.Id}/download")).Should().Be("Retained");
    }
    [Theory]
    [InlineData("NaN", "2026-10-01", null)]
    [InlineData("0.12345678901234567890123456789", "2026-10-01", null)]
    [InlineData("1.234,56", "2026-10-01", null)]
    [InlineData("1", "2026-02-30", null)]
    [InlineData("1", "2026-10-01", "123")]
    public async Task InvalidInvoiceInputIsRejected(string amount, string date, string? currency)
    {
        using var client = await Client(); var input = Input(); input.Amount = amount; input.InvoiceDate = date; input.Currency = currency;
        (await client.PostAsJsonAsync("/api/invoices", input)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
