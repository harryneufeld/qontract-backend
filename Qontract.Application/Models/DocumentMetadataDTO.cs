namespace Qontract.Application.Models;

/// <summary>
/// Payload for PATCH /api/documents/{id}/metadata (app issue #5): the
/// user-editable title and document date. Both fields are optional —
/// a null value leaves the current value untouched; an empty/blank
/// title clears the stored title (UI then falls back to the file name).
/// </summary>
public class DocumentMetadataDTO
{
    public string? Title { get; set; }

    public DateTime? DocumentDate { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
}
