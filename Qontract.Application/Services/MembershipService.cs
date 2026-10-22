using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Qontract.Application.Interfaces;
using Qontract.Application.Models;
using Qontract.Core.Security;
using Qontract.Domain.Models;

namespace Qontract.Application.Services;

public class MembershipService(IApplicationDbContext context, IConfiguration configuration)
{
    public bool AllowPublicRegistration => bool.TryParse(configuration["Registration:AllowPublic"], out var enabled) && enabled;

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    private static string HashCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant())));

    public Task<List<MembershipDTO>> ListAsync(int userId, CancellationToken ct = default) =>
        context.TenantMemberships.Where(m => m.UserId == userId && m.IsActive && m.User.IsActive && m.Tenant.IsActive)
            .OrderBy(m => m.Tenant.Name).Select(m => new MembershipDTO
            { TenantId = m.TenantId, Name = m.Tenant.Name, Type = m.Tenant.Type, Role = m.Role }).ToListAsync(ct);

    public async Task RequireAdmin(int userId, int tenantId, CancellationToken ct = default)
    {
        if (!await context.TenantMemberships.AnyAsync(m => m.UserId == userId && m.TenantId == tenantId
            && m.IsActive && m.Role == UserRole.TenantAdmin && m.User.IsActive && m.Tenant.IsActive, ct))
            throw new KeyNotFoundException("Tenant not found.");
    }

    public async Task<InvitationDTO> InviteAsync(int userId, int tenantId, InvitationCreateDTO dto, string? actor, CancellationToken ct = default)
    {
        await RequireAdmin(userId, tenantId, ct);
        // (issue #80) Only members sign in; the legacy Contractor role grants nothing.
        if (dto.Role is not (UserRole.TenantAdmin or UserRole.User)) throw new InvalidOperationException("Invalid role.");
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var invitation = new TenantInvitation
        {
            TenantId = tenantId, CodeHash = HashCode(code),
            Email = string.IsNullOrWhiteSpace(dto.Email) ? null : NormalizeEmail(dto.Email),
            Role = dto.Role, ExpiresAt = DateTime.UtcNow.AddDays(dto.ValidDays), CreatedBy = actor, ModifiedBy = actor
        };
        context.TenantInvitations.Add(invitation);
        await context.SaveChangesAsync(ct);
        var result = ToDto(invitation);
        result.Code = code; // Display once; only its hash is persisted.
        return result;
    }

    public async Task<List<InvitationDTO>> InvitationsAsync(int userId, int tenantId, CancellationToken ct = default)
    {
        await RequireAdmin(userId, tenantId, ct);
        var invitations = await context.TenantInvitations.Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
        return invitations.Select(ToDto).ToList();
    }

    public async Task RevokeAsync(int userId, int tenantId, Guid invitationId, string? actor, CancellationToken ct = default)
    {
        await using var transaction = await context.BeginSerializableTransactionAsync(ct);
        await RequireAdmin(userId, tenantId, ct);
        var invitation = await context.TenantInvitations.SingleOrDefaultAsync(i => i.Id == invitationId && i.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("Invitation not found.");
        if (invitation.RedeemedAt is not null) throw new InvalidOperationException("Invitation has already been used.");
        invitation.RevokedAt ??= DateTime.UtcNow;
        invitation.ModifiedBy = actor;
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<TenantInvitation> ValidInvitation(string code, string email, CancellationToken ct)
    {
        var hash = HashCode(code);
        var now = DateTime.UtcNow;
        var invitation = await context.TenantInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.CodeHash == hash
            && i.RevokedAt == null && i.RedeemedAt == null && i.ExpiresAt > now && i.Tenant.IsActive, ct);
        if (invitation is null || (invitation.Email is not null && invitation.Email != NormalizeEmail(email)))
            throw new InvalidOperationException("Invitation is invalid, expired, or unavailable for this account.");
        return invitation;
    }

    public async Task<(int UserId, int TenantId)> RegisterAsync(RegisterDTO dto, CancellationToken ct = default)
    {
        var invited = !string.IsNullOrWhiteSpace(dto.InvitationCode);
        if (!invited && !AllowPublicRegistration) throw new UnauthorizedAccessException("Public registration is disabled.");
        var terms = new TermsService(context, configuration);
        terms.EnsureAccepted(dto.AcceptedTermsVersion);
        var normalized = NormalizeEmail(dto.Email);
        // Hash before opening the database transaction to avoid holding locks during password work.
        var passwordHash = Passwords.Hash(dto.Password);
        await using var transaction = await context.BeginSerializableTransactionAsync(ct);
        if (await context.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            throw new InvalidOperationException("An account already exists. Sign in to accept an invitation.");
        TenantInvitation? invitation = invited ? await ValidInvitation(dto.InvitationCode!, dto.Email, ct) : null;
        int tenantId;
        if (invitation is null && string.IsNullOrWhiteSpace(dto.TenantName))
            tenantId = 0; // Personal workspace only: every account has one, no shared workspace is created.
        else if (invitation is null)
        {
            var tenant = new Tenant { Name = dto.TenantName!.Trim(), Type = dto.TenantType, IsActive = true, CreatedBy = dto.Email.Trim() };
            context.Tenants.Add(tenant);
            tenantId = tenant.Id;
        }
        else tenantId = invitation.TenantId;
        var user = new User
        {
            Name = string.IsNullOrWhiteSpace(dto.Name) ? DefaultName(dto.Email) : dto.Name.Trim(),
            Email = dto.Email.Trim(), PasswordHash = passwordHash,
            TenantId = tenantId == 0 ? null : tenantId, Role = invitation?.Role ?? UserRole.TenantAdmin, IsActive = true,
            CreatedBy = dto.Email.Trim(), ModifiedBy = dto.Email.Trim()
        };
        context.Users.Add(user); // The write hook creates its primary membership atomically.
        terms.Record(user, DateTime.UtcNow);
        await context.SaveChangesAsync(ct);
        if (invitation is not null && !await context.TryRedeemInvitationAsync(invitation.Id, user.Id, DateTime.UtcNow, ct))
            throw new InvalidOperationException("Invitation is no longer available.");
        await transaction.CommitAsync(ct);
        return (user.Id, tenantId);
    }

    // Placeholder shown until first-run setup asks for the real name.
    private static string DefaultName(string email)
    {
        var local = email.Trim().Split('@')[0];
        return local.Length is > 0 and <= 100 ? local : "qontract";
    }

    public async Task<int> RedeemAsync(int userId, string code, CancellationToken ct = default)
    {
        await using var transaction = await context.BeginSerializableTransactionAsync(ct);
        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new UnauthorizedAccessException();
        var invitation = await ValidInvitation(code, user.Email, ct);
        var membership = await context.TenantMemberships.SingleOrDefaultAsync(m => m.UserId == userId && m.TenantId == invitation.TenantId, ct);
        if (membership?.IsActive == true) throw new InvalidOperationException("You already belong to this tenant.");
        if (membership is null)
        {
            membership = new TenantMembership { UserId = userId, TenantId = invitation.TenantId, CreatedBy = user.Email };
            context.TenantMemberships.Add(membership);
        }
        membership.IsActive = true;
        membership.Role = invitation.Role;
        membership.ModifiedBy = user.Email;
        if (!await context.TryRedeemInvitationAsync(invitation.Id, userId, DateTime.UtcNow, ct))
            throw new InvalidOperationException("Invitation is no longer available.");
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return invitation.TenantId;
    }

    private static InvitationDTO ToDto(TenantInvitation i) => new()
    { Id = i.Id, TenantId = i.TenantId, Email = i.Email, Role = i.Role, ExpiresAt = i.ExpiresAt, RevokedAt = i.RevokedAt, RedeemedAt = i.RedeemedAt };
}
