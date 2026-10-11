using Microsoft.EntityFrameworkCore;
using Qontract.Infrastructure.Data;

namespace Qontract.Infrastructure;

/// <summary>Purges expired contract trash, retaining invoice evidence and shared files.</summary>
public class ContractPurgeService(ApplicationDbContext db)
{
    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var ids = await db.Contracts.Where(c => c.DeletedAt != null && c.RetentionExpiresAt <= now)
            .Select(c => c.Id).ToListAsync(ct);
        var count = 0;
        foreach (var id in ids)
        {
            await using var transaction = await db.BeginSerializableTransactionAsync(ct);
            var contract = await db.Contracts.SingleOrDefaultAsync(c => c.Id == id && c.DeletedAt != null && c.RetentionExpiresAt <= now, ct);
            if (contract is null) continue;
            // Invoice records/evidence must never be destroyed by contract cleanup.
            if (await db.Invoices.AnyAsync(i => i.ContractId == id || i.Document != null && i.Document.ContractId == id, ct)) continue;
            var documents = await db.Documents.Where(d => d.ContractId == id).Include(d => d.Links).ToListAsync(ct);
            foreach (var document in documents)
            {
                document.ContractId = document.Links.Where(l => l.ContractId != id).Select(l => (int?)l.ContractId).FirstOrDefault();
                if (document.ContractId is null)
                {
                    // Reuse document retention, which protects shared paths and invoice evidence.
                    document.IsDeleted = true;
                    document.DeletedAt ??= now;
                }
            }
            db.DocumentLinks.RemoveRange(await db.DocumentLinks.Where(l => l.ContractId == id).ToListAsync(ct));
            db.ContractPrices.RemoveRange(await db.ContractPrices.Where(p => p.ContractId == id).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            db.Contracts.Remove(contract);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            count++;
        }
        return count;
    }
}
