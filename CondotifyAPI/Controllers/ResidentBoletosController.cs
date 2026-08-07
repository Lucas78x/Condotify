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
            .Where(x => x.Batch.Status == BoletoBatchStatusEnum.Published &&
                        x.UnitId.HasValue && grant.UnitIds.Contains(x.UnitId.Value))
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
            .FirstOrDefaultAsync(x =>
                x.Id == documentId &&
                x.Batch.Status == BoletoBatchStatusEnum.Published &&
                x.Batch.LicenseId == grant.LicenseId, cancellationToken);
        if (document is null || !document.UnitId.HasValue || !grant.UnitIds.Contains(document.UnitId.Value))
            return NotFound();

        var bytes = await store.ReadAsync(grant.LicenseId, document.StorageReference, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }
}
