using Qontract.Domain.Models;

namespace Qontract.Application.Services;

// Zero is the authenticated personal workspace, never a persisted tenant ID.
// Audit actors and Contractor.UserId are not ownership evidence.
internal static class RecordAccess
{
    public static IQueryable<Contract> VisibleIn(this IQueryable<Contract> rows, int tenantId, int? userId) =>
        rows.Where(c => tenantId > 0 && c.TenantId == tenantId && c.OwnerUserId == null
            || tenantId == 0 && userId > 0 && c.TenantId == null && c.OwnerUserId == userId);

    public static IQueryable<Contractor> VisibleIn(this IQueryable<Contractor> rows, int tenantId, int? userId) =>
        rows.Where(c => tenantId > 0 && c.TenantId == tenantId && c.OwnerUserId == null
            || tenantId == 0 && userId > 0 && c.TenantId == null && c.OwnerUserId == userId);

    public static int? Owner(int tenantId, int? userId)
    {
        if (tenantId > 0) return null;
        if (tenantId == 0 && userId > 0) return userId;
        throw new UnauthorizedAccessException("An authenticated workspace owner is required.");
    }
}
