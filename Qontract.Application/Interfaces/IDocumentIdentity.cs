namespace Qontract.Application.Interfaces;

// Supplied by the authenticated request, never by a document payload.
public interface IDocumentIdentity
{
    int? UserId { get; }
}
