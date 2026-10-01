using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Qontract.Application.Models;
using Qontract.Domain.Models;

namespace Qontract.Application.Services;

public class InvoiceService(IApplicationDbContext db)
{
    public async Task<List<InvoiceDTO>> List(int tenantId, int? contractId, bool deleted, int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > 200) throw new InvalidOperationException("Invalid page size or page.");
        if (contractId.HasValue && !await db.Contracts.AnyAsync(c => c.Id == contractId && c.TenantId == tenantId))
            throw new KeyNotFoundException("Contract not found.");
        var rows = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenantId && i.IsDeleted == deleted
                && (!contractId.HasValue || i.ContractId == contractId))
            .OrderByDescending(i => i.InvoiceDate).ThenBy(i => i.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    public async Task<InvoiceDTO> Get(Guid id, int tenantId) => ToDto(await Find(id, tenantId));
    private async Task<Invoice> Find(Guid id, int tenantId) =>
        await db.Invoices.SingleOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId)
        ?? throw new KeyNotFoundException("Invoice not found.");

    public async Task<InvoiceDTO> Save(Guid? id, InvoiceInput input, int tenantId, string? actor)
    {
        await using var transaction = await db.BeginSerializableTransactionAsync();
        var invoice = id.HasValue ? await Find(id.Value, tenantId) : new Invoice { TenantId = tenantId, CreatedBy = actor };
        if (invoice.IsDeleted) throw new InvalidOperationException("Restore the invoice before editing it.");
        var date = ParseDate(input.InvoiceDate);
        var due = string.IsNullOrWhiteSpace(input.DueDate) ? (DateOnly?)null : ParseDate(input.DueDate);
        var amountText = input.Amount.Trim().Replace(',', '.');
        if (!Regex.IsMatch(amountText, @"^[+-]?\d+(\.\d+)?$") || !decimal.TryParse(amountText,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || CanonicalAmount(amountText) != CanonicalAmount(amount.ToString("0.############################", CultureInfo.InvariantCulture)))
            throw new InvalidOperationException("Amount must be a decimal without grouping or currency symbols.");
        var currency = Blank(input.Currency)?.ToUpperInvariant();
        if (currency is not null && !Regex.IsMatch(currency, "^[A-Z]{3}$"))
            throw new InvalidOperationException("Currency must be a three-letter code or left unknown.");
        if (input.InvoiceNumber?.Length > 100 || input.Description?.Length > 2000)
            throw new InvalidOperationException("Invoice number or description is too long.");
        if (input.ContractId.HasValue && !await db.Contracts.AnyAsync(c => c.Id == input.ContractId && c.TenantId == tenantId))
            throw new KeyNotFoundException("Contract not found.");
        if (input.DocumentId.HasValue)
        {
            var document = await db.Documents.Include(d => d.Links).SingleOrDefaultAsync(d => d.Id == input.DocumentId
                && d.Contract.TenantId == tenantId && !d.IsDeleted);
            if (document is null || (input.ContractId.HasValue && document.ContractId != input.ContractId
                    && !document.Links.Any(l => l.ContractId == input.ContractId)))
                throw new KeyNotFoundException("Document not found for this invoice.");
        }
        invoice.InvoiceDate = date; invoice.DueDate = due; invoice.Amount = amount;
        invoice.Currency = currency; invoice.InvoiceNumber = Blank(input.InvoiceNumber); invoice.Description = Blank(input.Description);
        invoice.ContractId = input.ContractId; invoice.DocumentId = input.DocumentId; invoice.ModifiedBy = actor;
        if (!id.HasValue) db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ToDto(invoice);
    }

    public async Task<InvoiceDTO> SetDeleted(Guid id, int tenantId, bool deleted, string? actor)
    {
        await using var transaction = await db.BeginSerializableTransactionAsync();
        var invoice = await Find(id, tenantId);
        if (!deleted && invoice.DocumentId.HasValue && !await db.Documents.AnyAsync(d => d.Id == invoice.DocumentId && !d.IsDeleted))
            throw new InvalidOperationException("Restore the invoice document first.");
        invoice.IsDeleted = deleted; invoice.DeletedAt = deleted ? DateTime.UtcNow : null; invoice.ModifiedBy = actor;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ToDto(invoice);
    }
    private static DateOnly ParseDate(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var date) ? date : throw new InvalidOperationException("Dates must use yyyy-MM-dd.");
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string CanonicalAmount(string value)
    {
        var negative = value.StartsWith('-');
        var parts = value.TrimStart('+', '-').Split('.');
        var whole = parts[0].TrimStart('0');
        var fraction = parts.Length > 1 ? parts[1].TrimEnd('0') : "";
        if (whole.Length == 0) whole = "0";
        return (negative && (whole != "0" || fraction.Length > 0) ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "");
    }
    private static InvoiceDTO ToDto(Invoice i) => new()
    {
        Id = i.Id, InvoiceDate = i.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DueDate = i.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Amount = i.Amount.ToString(CultureInfo.InvariantCulture),
        Currency = i.Currency, InvoiceNumber = i.InvoiceNumber, Description = i.Description,
        ContractId = i.ContractId, DocumentId = i.DocumentId, IsDeleted = i.IsDeleted, DeletedAt = i.DeletedAt,
        CreatedAt = i.CreatedAt, ModifiedAt = i.ModifiedAt
    };
}
