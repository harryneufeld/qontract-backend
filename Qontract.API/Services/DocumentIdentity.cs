using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Qontract.Application.Interfaces;

namespace Qontract.API.Services;

public class DocumentIdentity(IHttpContextAccessor accessor) : IDocumentIdentity
{
    public int? UserId => int.TryParse(accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
