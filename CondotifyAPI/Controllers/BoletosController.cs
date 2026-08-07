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
        if (form.DueDate == default)
            return BadRequest(new { Result = "InvalidDueDate", Errors = "Informe a data de vencimento do lote." });

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
            DueDate = AsUtcDate(form.DueDate),
            UploadedByUserId = actorId,
            UploadedByName = actorName,
            Status = BoletoBatchStatusEnum.Processing,
            SourceFileName = Path.GetFileName(form.File.FileName),
            TotalPages = pageCount,
            CreatedAt = now
        };
        context.BoletoBatches.Add(batch);

        // Um candidato por morador: se ele tiver mais de um vinculo valido nesta
        // licenca, usa o vinculo principal (IsPrimary) - nunca gera mais de uma
        // unidade candidata para o mesmo CPF por conta propria.
        // O vinculo tem que estar VIGENTE (mesma regra de
        // ResidentAuthorizationService.LinkIsCurrentlyValid, aqui reescrita em
        // comparacoes escalares para continuar traduzivel pelo EF): um morador
        // que ja mudou de unidade nao pode ser candidato da unidade antiga.
        var residents = (await context.Residents.AsNoTracking()
            .Where(x => x.CPF != "" && x.UnitLinks.Any(link =>
                link.IsActive && link.StartsAt <= now && (link.EndsAt == null || link.EndsAt > now) &&
                link.Unit.Block.LicenseId == licenseId))
            .Select(x => new
            {
                x.Id,
                x.CPF,
                UnitId = x.UnitLinks
                    .Where(link =>
                        link.IsActive && link.StartsAt <= now && (link.EndsAt == null || link.EndsAt > now) &&
                        link.Unit.Block.LicenseId == licenseId)
                    .OrderByDescending(link => link.IsPrimary)
                    .Select(link => link.UnitId)
                    .First()
            })
            .ToListAsync(cancellationToken))
            // CPF e gravado formatado ("123.456.789-01") pela maioria dos fluxos de
            // cadastro; o matcher compara contra digitos extraidos da pagina, entao
            // normaliza aqui (ja em memoria, fora do IQueryable).
            .Select(x => new BoletoPageMatcher.ResidentCandidate(x.Id, DigitsOnly(x.CPF), x.UnitId))
            .ToList();

        var units = (await context.Units.AsNoTracking()
            .Where(x => x.Block.LicenseId == licenseId)
            .Select(x => new { x.Id, BlockName = x.Block.Name, x.Number })
            .ToListAsync(cancellationToken))
            .Select(x => new BoletoPageMatcher.UnitCandidate(x.Id, x.BlockName, x.Number))
            .ToList();

        // Arquivos ja gravados neste lote: se qualquer pagina falhar no meio do
        // caminho, apaga tudo antes de sair para nao deixar arquivo orfao no disco
        // (nada foi persistido no banco ainda - SaveChangesAsync so vem depois).
        var storedReferences = new List<string>(pageCount);

        for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
        {
            var pageText = SafeExtractText(sourceBytes, pageNumber);
            var match = BoletoPageMatcher.Match(pageText, residents, units);

            string reference;
            try
            {
                // PdfPig (CountPages/ExtractPageText) e PDFsharp (ExtractPageAsPdf)
                // toleram estruturas malformadas de formas diferentes: um PDF que
                // abriu para contagem ainda pode explodir no split.
                var pagePdf = pdf.ExtractPageAsPdf(sourceBytes, pageNumber);
                reference = await store.StoreAsync(licenseId, pagePdf, cancellationToken);
            }
            catch (Exception)
            {
                foreach (var stored in storedReferences)
                    await store.DeleteAsync(licenseId, stored, cancellationToken);
                return BadRequest(new { Result = "InvalidPdf", Errors = "Nao foi possivel ler o PDF enviado." });
            }
            storedReferences.Add(reference);

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

    [HttpPost("single")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadSingle(Guid licenseId, [FromForm] BoletoSingleUploadForm form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.Reference) || form.Reference.Length > 80)
            return BadRequest(new { Result = "InvalidReference", Errors = "Informe uma referencia valida." });
        if (form.File is null || form.File.Length == 0)
            return BadRequest(new { Result = "FileRequired", Errors = "Selecione o PDF do boleto." });
        if (!string.Equals(form.File.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Result = "InvalidFileType", Errors = "O arquivo deve ser um PDF." });
        if (form.DueDate == default)
            return BadRequest(new { Result = "InvalidDueDate", Errors = "Informe a data de vencimento." });

        var unitExists = await context.Units.AsNoTracking().AnyAsync(x => x.Id == form.UnitId && x.Block.LicenseId == licenseId, cancellationToken);
        if (!unitExists) return BadRequest(new { Result = "InvalidUnit", Errors = "Unidade invalida para esta licenca." });

        await using var stream = new MemoryStream();
        await form.File.CopyToAsync(stream, cancellationToken);
        var sourceBytes = stream.ToArray();

        // Ao contrario do lote (uma pagina = uma unidade, exige separar por
        // pagina para casar cada uma com uma unidade diferente), aqui a
        // unidade ja foi escolhida manualmente: o PDF inteiro - com quantas
        // paginas tiver (capa, anexo, boleto em si) - pertence a essa unica
        // unidade, entao e guardado sem dividir.
        try
        {
            pdf.CountPages(sourceBytes);
        }
        catch (Exception)
        {
            return BadRequest(new { Result = "InvalidPdf", Errors = "Nao foi possivel ler o PDF enviado." });
        }

        var actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedActor) ? parsedActor : Guid.Empty;
        var actorName = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administracao";
        var now = DateTime.UtcNow;

        string reference;
        try
        {
            reference = await store.StoreAsync(licenseId, sourceBytes, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { Result = "FileTooLarge", Errors = "O PDF do boleto deve ter no maximo 2 MB." });
        }
        catch (Exception)
        {
            return BadRequest(new { Result = "InvalidPdf", Errors = "Nao foi possivel ler o PDF enviado." });
        }

        var batch = new BoletoBatchDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Reference = form.Reference.Trim(),
            DueDate = AsUtcDate(form.DueDate),
            UploadedByUserId = actorId,
            UploadedByName = actorName,
            Status = BoletoBatchStatusEnum.Published,
            SourceFileName = Path.GetFileName(form.File.FileName),
            TotalPages = 1,
            CreatedAt = now,
            PublishedAt = now
        };
        context.BoletoBatches.Add(batch);

        var document = new BoletoDocumentDTO
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            UnitId = form.UnitId,
            PageNumber = 1,
            MatchMethod = BoletoMatchMethodEnum.Manual,
            Ignored = false,
            StorageReference = reference,
            ExtractedSnippet = Snippet(SafeExtractText(sourceBytes, 1)),
            CreatedAt = now
        };
        context.BoletoDocuments.Add(document);
        await context.SaveChangesAsync(cancellationToken);

        // Carrega o vinculo da unidade so para resolver os destinatarios da
        // notificacao - reaproveita ResolveNotificationTargets em vez de
        // duplicar a regra de vinculo vigente (mesma logica de Publish).
        document.Unit = await context.Units.AsNoTracking()
            .Include(x => x.ResidentLinks)
            .FirstOrDefaultAsync(x => x.Id == form.UnitId, cancellationToken);
        foreach (var (residentId, deduplicationKey) in ResolveNotificationTargets(document, now))
        {
            await notifier.NotifyResidentAsync(
                residentId,
                MobileNotificationCategory.Financial,
                "Novo boleto disponivel",
                $"Seu boleto de {batch.Reference} ja esta disponivel.",
                "/boletos",
                deduplicationKey,
                cancellationToken);
        }

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

        var notifiedAt = DateTime.UtcNow;
        foreach (var document in toPublish)
        {
            foreach (var (residentId, deduplicationKey) in ResolveNotificationTargets(document, notifiedAt))
            {
                await notifier.NotifyResidentAsync(
                    residentId,
                    MobileNotificationCategory.Financial,
                    "Novo boleto disponivel",
                    $"Seu boleto de {batch.Reference} ja esta disponivel.",
                    "/boletos",
                    deduplicationKey,
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

        // Materializado antes de remover: marcar as entidades como Deleted dispara
        // fixup que pode mexer em batch.Documents no meio da iteracao.
        var documents = batch.Documents.ToList();
        foreach (var document in documents)
            await store.DeleteAsync(licenseId, document.StorageReference, cancellationToken);

        // Cancelamento e logico, nao apaga o lote: as paginas e os arquivos somem
        // (nunca foram visiveis para o morador), mas a linha do lote fica como
        // Cancelled. Isso preserva o historico e mantem o explicativo animado de
        // primeiro acesso escondido - o gatilho dele e a existencia de QUALQUER
        // lote, "mesmo que so criado e depois cancelado" (spec).
        context.BoletoDocuments.RemoveRange(documents);
        batch.Status = BoletoBatchStatusEnum.Cancelled;
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

    /// <summary>
    /// CPF é gravado ora só com dígitos (importação CSV), ora formatado
    /// ("123.456.789-01") por todos os outros fluxos de cadastro. O matcher compara
    /// contra dígitos extraídos do texto da página, então o candidato tem que chegar
    /// normalizado. Aplicado depois da materialização da query (LINQ em memória).
    /// </summary>
    internal static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    /// <summary>
    /// Datas vindas do date picker do portal chegam com Kind=Unspecified (o cliente
    /// serializa "O" sem offset) e a coluna DueDate é 'timestamp with time zone', que
    /// o Npgsql recusa para Kind != Utc. É uma data de calendário sem fuso relevante,
    /// então SpecifyKind (e não ToUniversalTime) é o correto: reinterpreta o mesmo dia
    /// como UTC sem deslocá-lo. Mesma convenção de AmenitiesController.AsUtcDate.
    /// </summary>
    internal static DateTime AsUtcDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    /// <summary>
    /// Quem deve ser notificado sobre um boleto recém-publicado e com qual chave de
    /// deduplicação. Só entram moradores com vínculo VIGENTE na unidade — mesma regra
    /// de <see cref="ResidentAuthorizationService.LinkIsCurrentlyValid"/>: um morador
    /// que já saiu da unidade (link ainda IsActive mas com EndsAt no passado) não pode
    /// receber push de um boleto que ele nem consegue abrir. Puro e recebendo
    /// <paramref name="now"/> explicitamente para ser testável sem banco.
    /// </summary>
    internal static IEnumerable<(Guid ResidentId, string DeduplicationKey)> ResolveNotificationTargets(
        BoletoDocumentDTO document,
        DateTime now)
    {
        var links = document.Unit?.ResidentLinks;
        if (links is null) return [];

        var deduplicationKey = $"boleto-published:{document.Id:N}";
        return links
            .Where(link => ResidentAuthorizationService.LinkIsCurrentlyValid(link, now))
            .Select(link => link.ResidentId)
            .Distinct()
            .Select(residentId => (residentId, deduplicationKey))
            .ToList();
    }

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
