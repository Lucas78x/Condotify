using System.Security.Claims;
using CondotifyAPI.Data.Finance;
using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Finance;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/boletos")]
public sealed class BoletosController(
    DatabaseContext context,
    IBoletoDocumentStore store,
    IBoletoPdfProcessor pdf,
    IPlatformPushNotifier notifier) : ControllerBase
{
    private const int MaxPages = 600;

    [HttpPost("batches")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadBatch(Guid licenseId, [FromForm] BoletoBatchUploadForm form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.Reference) || form.Reference.Length > 80)
            return BadRequest(new { Result = "InvalidReference", Errors = "Informe uma referencia valida para o lote." });
        if (form.File is null || form.File.Length == 0)
            return BadRequest(new { Result = "FileRequired", Errors = "Selecione o PDF com os boletos." });
        if (!string.Equals(form.File.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Result = "InvalidFileType", Errors = "O arquivo deve ser um PDF." });

        await using var stream = new MemoryStream();
        await form.File.CopyToAsync(stream, cancellationToken);
        var sourceBytes = stream.ToArray();

        int pageCount;
        try
        {
            pageCount = pdf.CountPages(sourceBytes);
        }
        catch (Exception)
        {
            return BadRequest(new { Result = "InvalidPdf", Errors = "Nao foi possivel ler o PDF enviado." });
        }
        if (pageCount == 0 || pageCount > MaxPages)
            return BadRequest(new { Result = "InvalidPageCount", Errors = $"O PDF deve ter entre 1 e {MaxPages} paginas." });

        var actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedActor) ? parsedActor : Guid.Empty;
        var actorName = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administracao";
        var now = DateTime.UtcNow;

        var batch = new BoletoBatchDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Reference = form.Reference.Trim(),
            DueDate = form.DueDate,
            UploadedByUserId = actorId,
            UploadedByName = actorName,
            Status = BoletoBatchStatusEnum.Processing,
            SourceFileName = Path.GetFileName(form.File.FileName),
            TotalPages = pageCount,
            CreatedAt = now
        };
        context.BoletoBatches.Add(batch);

        // Um candidato por morador: se ele tiver mais de um vinculo ativo nesta
        // licenca, usa o vinculo principal (IsPrimary) - nunca gera mais de uma
        // unidade candidata para o mesmo CPF por conta propria.
        var residents = (await context.Residents.AsNoTracking()
            .Where(x => x.CPF != "" && x.UnitLinks.Any(link => link.IsActive && link.Unit.Block.LicenseId == licenseId))
            .Select(x => new
            {
                x.Id,
                x.CPF,
                UnitId = x.UnitLinks
                    .Where(link => link.IsActive && link.Unit.Block.LicenseId == licenseId)
                    .OrderByDescending(link => link.IsPrimary)
                    .Select(link => link.UnitId)
                    .First()
            })
            .ToListAsync(cancellationToken))
            .Select(x => new BoletoPageMatcher.ResidentCandidate(x.Id, x.CPF, x.UnitId))
            .ToList();

        var units = (await context.Units.AsNoTracking()
            .Where(x => x.Block.LicenseId == licenseId)
            .Select(x => new { x.Id, BlockName = x.Block.Name, x.Number })
            .ToListAsync(cancellationToken))
            .Select(x => new BoletoPageMatcher.UnitCandidate(x.Id, x.BlockName, x.Number))
            .ToList();

        for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
        {
            var pageText = SafeExtractText(sourceBytes, pageNumber);
            var match = BoletoPageMatcher.Match(pageText, residents, units);
            var pagePdf = pdf.ExtractPageAsPdf(sourceBytes, pageNumber);
            var reference = await store.StoreAsync(licenseId, pagePdf, cancellationToken);

            context.BoletoDocuments.Add(new BoletoDocumentDTO
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                UnitId = match.UnitId,
                PageNumber = pageNumber,
                MatchMethod = match.Method,
                Ignored = false,
                StorageReference = reference,
                ExtractedSnippet = Snippet(pageText),
                CreatedAt = now
            });
        }

        batch.Status = BoletoBatchStatusEnum.PendingReview;
        await context.SaveChangesAsync(cancellationToken);

        var detail = await LoadDetailAsync(licenseId, batch.Id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("batches")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> ListBatches(Guid licenseId, CancellationToken cancellationToken)
    {
        var batches = await context.BoletoBatches.AsNoTracking()
            .Include(x => x.Documents)
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(batches.Select(x => ToBatchOut(x, x.Documents)).ToList());
    }

    [HttpGet("batches/{batchId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> GetBatch(Guid licenseId, Guid batchId, CancellationToken cancellationToken)
    {
        var detail = await LoadDetailAsync(licenseId, batchId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("documents/{documentId:guid}/file")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> GetDocumentFile(Guid licenseId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await context.BoletoDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId && x.Batch.LicenseId == licenseId, cancellationToken);
        if (document is null) return NotFound();

        var bytes = await store.ReadAsync(licenseId, document.StorageReference, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }

    [HttpPut("documents/{documentId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> UpdateDocument(Guid licenseId, Guid documentId, [FromBody] BoletoDocumentUpdateIn input, CancellationToken cancellationToken)
    {
        var document = await context.BoletoDocuments
            .Include(x => x.Batch)
            .FirstOrDefaultAsync(x => x.Id == documentId && x.Batch.LicenseId == licenseId, cancellationToken);
        if (document is null) return NotFound();
        if (document.Batch.Status != BoletoBatchStatusEnum.PendingReview)
            return Conflict(new { Result = "BatchNotEditable", Errors = "So e possivel editar um lote em revisao." });

        if (input.UnitId.HasValue)
        {
            var unitExists = await context.Units.AsNoTracking().AnyAsync(x => x.Id == input.UnitId && x.Block.LicenseId == licenseId, cancellationToken);
            if (!unitExists) return BadRequest(new { Result = "InvalidUnit", Errors = "Unidade invalida para esta licenca." });
        }

        document.UnitId = input.Ignored ? null : input.UnitId;
        document.Ignored = input.Ignored;
        if (input.UnitId.HasValue && !input.Ignored) document.MatchMethod = BoletoMatchMethodEnum.Manual;
        await context.SaveChangesAsync(cancellationToken);

        var detail = await LoadDetailAsync(licenseId, document.BatchId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("batches/{batchId:guid}/publish")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> Publish(Guid licenseId, Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await context.BoletoBatches
            .Include(x => x.Documents).ThenInclude(x => x.Unit).ThenInclude(x => x!.ResidentLinks)
            .FirstOrDefaultAsync(x => x.Id == batchId && x.LicenseId == licenseId, cancellationToken);
        if (batch is null) return NotFound();
        if (batch.Status != BoletoBatchStatusEnum.PendingReview)
            return Conflict(new { Result = "BatchNotReady", Errors = "Este lote nao esta aguardando revisao." });

        var pending = batch.Documents.Where(x => !x.Ignored && !x.UnitId.HasValue).ToList();
        if (pending.Count > 0)
            return UnprocessableEntity(new { Result = "PendingPages", Errors = $"{pending.Count} pagina(s) ainda sem unidade definida." });

        var toPublish = batch.Documents.Where(x => !x.Ignored).ToList();
        var duplicateUnits = toPublish
            .GroupBy(x => x.UnitId!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateUnits.Count > 0)
            return UnprocessableEntity(new { Result = "DuplicateUnits", Errors = $"{duplicateUnits.Count} unidade(s) com mais de um boleto neste lote. Corrija antes de publicar." });

        var toIgnore = batch.Documents.Where(x => x.Ignored).ToList();
        foreach (var document in toIgnore)
        {
            await store.DeleteAsync(licenseId, document.StorageReference, cancellationToken);
            context.BoletoDocuments.Remove(document);
        }

        batch.Status = BoletoBatchStatusEnum.Published;
        batch.PublishedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        foreach (var document in toPublish)
        {
            var residentIds = document.Unit?.ResidentLinks
                .Where(link => link.IsActive)
                .Select(link => link.ResidentId)
                .Distinct() ?? [];
            foreach (var residentId in residentIds)
            {
                await notifier.NotifyResidentAsync(
                    residentId,
                    MobileNotificationCategory.Financial,
                    "Novo boleto disponivel",
                    $"Seu boleto de {batch.Reference} ja esta disponivel.",
                    "/boletos",
                    $"boleto-published:{document.Id:N}",
                    cancellationToken);
            }
        }

        return Ok(new BoletoPublishResultOut { PublishedCount = toPublish.Count, IgnoredCount = toIgnore.Count });
    }

    [HttpPost("batches/{batchId:guid}/cancel")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> Cancel(Guid licenseId, Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await context.BoletoBatches
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == batchId && x.LicenseId == licenseId, cancellationToken);
        if (batch is null) return NotFound();
        if (batch.Status != BoletoBatchStatusEnum.PendingReview)
            return Conflict(new { Result = "BatchNotCancellable", Errors = "So e possivel cancelar um lote em revisao." });

        foreach (var document in batch.Documents)
            await store.DeleteAsync(licenseId, document.StorageReference, cancellationToken);

        context.BoletoBatches.Remove(batch);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(new { Result = "Cancelled" });
    }

    [HttpDelete("documents/{documentId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> DeleteDocument(Guid licenseId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await context.BoletoDocuments
            .Include(x => x.Batch)
            .FirstOrDefaultAsync(x => x.Id == documentId && x.Batch.LicenseId == licenseId, cancellationToken);
        if (document is null) return NotFound();
        if (document.Batch.Status != BoletoBatchStatusEnum.Published)
            return Conflict(new { Result = "BatchNotPublished", Errors = "Use cancelar o lote para remover paginas antes da publicacao." });

        await store.DeleteAsync(licenseId, document.StorageReference, cancellationToken);
        context.BoletoDocuments.Remove(document);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(new { Result = "Deleted" });
    }

    private async Task<BoletoBatchDetailOut?> LoadDetailAsync(Guid licenseId, Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await context.BoletoBatches.AsNoTracking()
            .Include(x => x.Documents).ThenInclude(x => x.Unit).ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.Id == batchId && x.LicenseId == licenseId, cancellationToken);
        if (batch is null) return null;

        return new BoletoBatchDetailOut
        {
            Batch = ToBatchOut(batch, batch.Documents),
            Documents = batch.Documents
                .OrderBy(x => x.PageNumber)
                .Select(ToDocumentOut)
                .ToList()
        };
    }

    private static BoletoBatchOut ToBatchOut(BoletoBatchDTO batch, ICollection<BoletoDocumentDTO> documents) => new()
    {
        Id = batch.Id,
        Reference = batch.Reference,
        DueDate = batch.DueDate,
        Status = batch.Status.ToString(),
        TotalPages = batch.TotalPages,
        MatchedCount = documents.Count(x => x.UnitId.HasValue),
        UnmatchedCount = documents.Count(x => !x.UnitId.HasValue && !x.Ignored),
        UploadedByName = batch.UploadedByName,
        CreatedAt = batch.CreatedAt,
        PublishedAt = batch.PublishedAt
    };

    private static BoletoDocumentOut ToDocumentOut(BoletoDocumentDTO document) => new()
    {
        Id = document.Id,
        PageNumber = document.PageNumber,
        UnitId = document.UnitId,
        UnitLabel = document.Unit is null ? string.Empty : $"{document.Unit.Block.Name} / {document.Unit.Number}".Trim(' ', '/'),
        MatchMethod = document.MatchMethod.ToString(),
        Ignored = document.Ignored,
        ExtractedSnippet = document.ExtractedSnippet
    };

    private string SafeExtractText(byte[] sourceBytes, int pageNumber)
    {
        try { return pdf.ExtractPageText(sourceBytes, pageNumber); }
        catch (Exception) { return string.Empty; }
    }

    private static string Snippet(string pageText)
    {
        var collapsed = string.Join(' ', pageText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > 240 ? collapsed[..240] : collapsed;
    }
}
