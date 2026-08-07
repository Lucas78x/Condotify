using System.Linq.Expressions;
using CondotifyAPI.Data.Finance;
using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize(Policy = "Resident")]
[Route("api/resident/boletos")]
public sealed class ResidentBoletosController(
    DatabaseContext context,
    IResidentAuthorizationService authorization,
    IBoletoDocumentStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        if (grant.UnitIds.Count == 0) return Ok(Array.Empty<ResidentBoletoOut>());

        var documents = await context.BoletoDocuments.AsNoTracking()
            .Include(x => x.Batch)
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .Where(IsVisibleTo(grant))
            .OrderByDescending(x => x.Batch.PublishedAt)
            .Select(x => new ResidentBoletoOut
            {
                DocumentId = x.Id,
                Reference = x.Batch.Reference,
                DueDate = x.Batch.DueDate,
                UnitLabel = (x.Unit!.Block.Name + " / " + x.Unit.Number).Trim(),
                PublishedAt = x.Batch.PublishedAt!.Value
            })
            .ToListAsync(cancellationToken);

        return Ok(documents);
    }

    [HttpGet("{documentId:guid}/file")]
    public async Task<IActionResult> Download(Guid documentId, CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var document = await context.BoletoDocuments.AsNoTracking()
            .Include(x => x.Batch)
            .Where(IsVisibleTo(grant))
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document is null) return NotFound();

        var bytes = await store.ReadAsync(grant.LicenseId, document.StorageReference, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }

    /// <summary>
    /// The single definition of "is this document visible to this resident" - a document is
    /// visible only when its batch is Published, belongs to the resident's licence, has a unit
    /// assigned, and that unit is one the resident currently has an active link to. Both
    /// <see cref="List"/> and <see cref="Download"/> compose this same expression into their EF
    /// query rather than each re-deriving the rule, so the two endpoints cannot silently drift
    /// apart. Returned as an expression tree (not a compiled delegate) so it stays
    /// EF-translatable when passed to <c>Where</c>; tests exercise it by compiling it and
    /// invoking it directly against plain in-memory objects - no database required.
    /// </summary>
    internal static Expression<Func<BoletoDocumentDTO, bool>> IsVisibleTo(ResidentAccessGrant grant) =>
        document =>
            document.Batch.Status == BoletoBatchStatusEnum.Published &&
            document.Batch.LicenseId == grant.LicenseId &&
            document.UnitId.HasValue &&
            grant.UnitIds.Contains(document.UnitId.Value);
}
