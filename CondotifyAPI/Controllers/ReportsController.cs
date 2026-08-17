using Condotify.Models;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Jwt;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/reports")]
public sealed class ReportsController(DatabaseContext context) : ControllerBase
{
    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ViewDashboard)]
    public async Task<IActionResult> Get(
        Guid licenseId,
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        days = NormalizePeriod(days);
        var now = DateTime.UtcNow;
        var since = DateTime.SpecifyKind(now.Date.AddDays(-(days - 1)), DateTimeKind.Utc);

        if (!await context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId, cancellationToken))
            return NotFound();

        var units = await context.Units.AsNoTracking()
            .Where(x => x.Block.LicenseId == licenseId)
            .Select(x => new UnitRow(x.Id, x.BlockId, x.Block.Name))
            .ToListAsync(cancellationToken);
        var unitIds = units.Select(x => x.Id).ToList();

        var residents = await context.Residents.AsNoTracking()
            .Where(x => !x.Temporary &&
                        x.AccessType != ResidentAccessTypeEnum.Guest &&
                        x.AccessType != ResidentAccessTypeEnum.ServiceProvider &&
                        (unitIds.Contains(x.UnitId) || x.UnitLinks.Any(link =>
                            unitIds.Contains(link.UnitId) && link.IsActive && link.StartsAt <= now &&
                            (!link.EndsAt.HasValue || link.EndsAt >= now))))
            .Select(x => new ResidentRow(
                x.Id,
                unitIds.Contains(x.UnitId)
                    ? x.UnitId
                    : x.UnitLinks.Where(link => unitIds.Contains(link.UnitId) && link.IsActive &&
                                                link.StartsAt <= now && (!link.EndsAt.HasValue || link.EndsAt >= now))
                        .OrderByDescending(link => link.IsPrimary)
                        .ThenBy(link => link.CreatedAt)
                        .Select(link => link.UnitId)
                        .FirstOrDefault(),
                x.AccessType,
                x.IsActive,
                x.Email != string.Empty,
                x.PhoneNumber != string.Empty,
                x.CPF != string.Empty || x.RG != string.Empty,
                x.ImgUrl != string.Empty,
                x.Password != string.Empty))
            .ToListAsync(cancellationToken);

        var residentIds = residents.Select(x => x.Id).ToList();
        var activeResidents = residents.Where(x => x.IsActive).ToList();
        var activeResidentIds = activeResidents.Select(x => x.Id).ToList();

        var appLinkedIds = await context.PushInstallations.AsNoTracking()
            .Where(x => x.SubjectType == PrincipalTypes.Resident && x.IsActive && activeResidentIds.Contains(x.SubjectId))
            .Select(x => x.SubjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var recentlyActiveIds = await context.RefreshTokens.AsNoTracking()
            .Where(x => x.SubjectType == PrincipalTypes.Resident && activeResidentIds.Contains(x.SubjectId) && x.CreatedAt >= now.AddDays(-30))
            .Select(x => x.SubjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var credentials = await context.ResidentAccessCredentials.AsNoTracking()
            .Where(x => activeResidentIds.Contains(x.ResidentId) && x.IsActive &&
                        (!x.IsTemporary || x.ValidTo >= now))
            .Select(x => new CredentialRow(x.ResidentId, x.CredentialType))
            .ToListAsync(cancellationToken);

        var vehicles = unitIds.Count == 0
            ? 0
            : await context.Vehicles.AsNoTracking().CountAsync(x => unitIds.Contains(x.UnitId) && x.IsActive, cancellationToken);

        var accessQuery = context.AccessEventRecords.AsNoTracking()
            .Where(x => x.LicenseId == licenseId && x.OccurredAt >= since && x.OccurredAt <= now);
        var accessSummary = await accessQuery.GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Authorized = group.Count(x => x.Authorized),
                Denied = group.Count(x => !x.Authorized)
            })
            .FirstOrDefaultAsync(cancellationToken);
        var accessDays = await accessQuery
            .GroupBy(x => new { x.OccurredAt.Year, x.OccurredAt.Month, x.OccurredAt.Day })
            .Select(group => new AccessDayRow(
                group.Key.Year,
                group.Key.Month,
                group.Key.Day,
                group.Count(x => x.Authorized),
                group.Count(x => !x.Authorized)))
            .ToListAsync(cancellationToken);
        var accessHours = await accessQuery
            .GroupBy(x => x.OccurredAt.Hour)
            .Select(group => new ReportHourViewModel
            {
                Hour = group.Key,
                Authorized = group.Count(x => x.Authorized),
                Denied = group.Count(x => !x.Authorized)
            })
            .ToListAsync(cancellationToken);

        var visitQuery = context.AccessVisits.AsNoTracking()
            .Where(x => x.LicenseId == licenseId && x.CreatedAt >= since && x.CreatedAt <= now);
        var visitRows = await visitQuery
            .GroupBy(x => new { x.CreatedAt.Year, x.CreatedAt.Month, x.CreatedAt.Day })
            .Select(group => new VisitDayRow(
                group.Key.Year,
                group.Key.Month,
                group.Key.Day,
                group.Count()))
            .ToListAsync(cancellationToken);
        var visitSummary = await visitQuery.GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                CheckedIn = group.Count(x => x.Status == AccessVisitStatusEnum.CheckedIn || x.Status == AccessVisitStatusEnum.CheckedOut),
                Pending = group.Count(x => x.Status == AccessVisitStatusEnum.Scheduled ||
                                           x.Status == AccessVisitStatusEnum.PendingApproval ||
                                           x.Status == AccessVisitStatusEnum.PendingEnrollment),
                Expired = group.Count(x => x.Status == AccessVisitStatusEnum.Expired)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var appLinked = appLinkedIds.ToHashSet();
        var recentlyActive = recentlyActiveIds.ToHashSet();
        var credentialResidentIds = credentials.Select(x => x.ResidentId).ToHashSet();
        var facialResidentIds = credentials.Where(x => x.Type == AccessCredentialTypeEnum.Face).Select(x => x.ResidentId).ToHashSet();
        var unitMap = units.ToDictionary(x => x.Id);
        var occupiedUnitIds = activeResidents.Select(x => x.UnitId).Where(unitMap.ContainsKey).ToHashSet();
        var accountsCreated = activeResidents.Count(x => x.HasAccount);
        var completeContacts = activeResidents.Count(x => x.HasEmail && x.HasPhone);
        var withDocuments = activeResidents.Count(x => x.HasDocument);
        var withPhoto = activeResidents.Count(x => x.HasPhoto);

        var output = new LicenseReportsViewModel
        {
            GeneratedAt = now,
            PeriodStart = since,
            PeriodEnd = now,
            PeriodDays = days,
            Residents = new ResidentReportSummaryViewModel
            {
                Registered = residents.Count,
                Active = activeResidents.Count,
                AccountsCreated = accountsCreated,
                AppLinked = appLinked.Count,
                RecentlyActive = recentlyActive.Count,
                WithCompleteContact = completeContacts,
                WithDocument = withDocuments,
                WithProfilePhoto = withPhoto,
                WithActiveCredential = credentialResidentIds.Count,
                WithFacialCredential = facialResidentIds.Count,
                AccountActivationRate = Percentage(accountsCreated, activeResidents.Count),
                AppAdoptionRate = Percentage(appLinked.Count, activeResidents.Count),
                RecentUsageRate = Percentage(recentlyActive.Count, activeResidents.Count)
            },
            Structure = new StructureReportSummaryViewModel
            {
                Blocks = units.Select(x => x.BlockId).Distinct().Count(),
                Units = units.Count,
                OccupiedUnits = occupiedUnitIds.Count,
                VacantUnits = Math.Max(0, units.Count - occupiedUnitIds.Count),
                Vehicles = vehicles,
                OccupancyRate = Percentage(occupiedUnitIds.Count, units.Count)
            },
            Operation = new OperationReportSummaryViewModel
            {
                AccessEvents = accessSummary?.Total ?? 0,
                AuthorizedAccesses = accessSummary?.Authorized ?? 0,
                DeniedAccesses = accessSummary?.Denied ?? 0,
                AuthorizationRate = Percentage(accessSummary?.Authorized ?? 0, accessSummary?.Total ?? 0),
                VisitorsCreated = visitSummary?.Total ?? 0,
                VisitorsCheckedIn = visitSummary?.CheckedIn ?? 0,
                VisitorsPending = visitSummary?.Pending ?? 0,
                VisitorsExpired = visitSummary?.Expired ?? 0,
                PeakHour = accessHours.OrderByDescending(x => x.Total).ThenBy(x => x.Hour).FirstOrDefault()?.Hour ?? 0
            },
            ResidentDistribution = BuildResidentDistribution(activeResidents),
            CredentialDistribution = BuildCredentialDistribution(credentials),
            AccessByHour = Enumerable.Range(0, 24)
                .Select(hour => accessHours.FirstOrDefault(x => x.Hour == hour) ?? new ReportHourViewModel { Hour = hour })
                .ToList()
        };

        output.AdoptionByBlock = BuildBlockAdoption(units, activeResidents, appLinked);
        output.Trend = BuildTrend(since, now, days, accessDays, visitRows);
        output.QualityIndicators = BuildQualityIndicators(licenseId, output, completeContacts, withDocuments, withPhoto,
            credentialResidentIds.Count, facialResidentIds.Count);
        output.QualityScore = QualityScore(output.QualityIndicators);
        output.AttentionItems = BuildAttentionItems(licenseId, output);

        return Ok(output);
    }

    [HttpGet("export/{format}")]
    [RequireLicensePermission(LicensePermissionEnum.ViewDashboard)]
    public async Task<IActionResult> Export(Guid licenseId, string format, [FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        var reportResult = await Get(licenseId, days, cancellationToken);
        if (reportResult is not OkObjectResult { Value: LicenseReportsViewModel report }) return reportResult;

        var license = await context.Licenses.AsNoTracking().Where(x => x.Id == licenseId)
            .Select(x => new { x.Name, x.Code }).FirstOrDefaultAsync(cancellationToken);
        if (license is null) return NotFound();

        var safeCode = new string((license.Code ?? "condominio").ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(safeCode)) safeCode = "condominio";
        var licenseCode = license.Code ?? "condominio";
        var prefix = $"relatorio-condotify-{safeCode}-{report.PeriodEnd:yyyyMMdd}";

        return format.Trim().ToLowerInvariant() switch
        {
            "xlsx" or "excel" => File(ReportExportService.CreateExcel(report, license.Name, licenseCode), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", prefix + ".xlsx"),
            "pdf" => File(ReportExportService.CreatePdf(report, license.Name, licenseCode), "application/pdf", prefix + ".pdf"),
            "csv" => File(ReportExportService.CreateCsv(report, license.Name, licenseCode), "text/csv; charset=utf-8", prefix + ".csv"),
            _ => BadRequest(new { Error = "Formato inválido. Use xlsx, pdf ou csv." })
        };
    }

    internal static int NormalizePeriod(int days) => days switch
    {
        <= 30 => 30,
        <= 90 => 90,
        <= 180 => 180,
        _ => 365
    };

    internal static decimal Percentage(int value, int total) => total <= 0
        ? 0
        : Math.Round(value * 100m / total, 1);

    internal static int QualityScore(IEnumerable<ReportQualityIndicatorViewModel> indicators)
    {
        var values = indicators.Select(x => x.Percentage).ToList();
        return values.Count == 0 ? 0 : (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero);
    }

    private static List<ReportQualityIndicatorViewModel> BuildQualityIndicators(
        Guid licenseId,
        LicenseReportsViewModel report,
        int completeContacts,
        int withDocuments,
        int withPhoto,
        int withCredential,
        int withFace)
    {
        var residents = report.Residents.Active;
        return
        [
            Quality("accounts", "Contas do aplicativo", "Moradores ativos com senha de acesso criada.", report.Residents.AccountsCreated, residents, $"/licencas/{licenseId}/estrutura"),
            Quality("app", "Aplicativo vinculado", "Moradores com um dispositivo ativo vinculado ao Condotify.", report.Residents.AppLinked, residents, $"/licencas/{licenseId}/estrutura"),
            Quality("contact", "Contato completo", "Cadastro com e-mail e telefone para comunicação.", completeContacts, residents, $"/licencas/{licenseId}/estrutura"),
            Quality("document", "Identificação cadastrada", "Moradores com CPF ou RG informado.", withDocuments, residents, $"/licencas/{licenseId}/estrutura"),
            Quality("credentials", "Credencial ativa", "Moradores cobertos por ao menos uma credencial válida.", withCredential, residents, $"/licencas/{licenseId}/credenciais"),
            Quality("face", "Facial habilitada", "Moradores com credencial facial ativa.", withFace, residents, $"/licencas/{licenseId}/credenciais"),
            Quality("photo", "Foto de perfil", "Cadastros com foto para conferência operacional.", withPhoto, residents, $"/licencas/{licenseId}/estrutura"),
            Quality("occupancy", "Ocupação mapeada", "Unidades com pelo menos um morador ativo vinculado.", report.Structure.OccupiedUnits, report.Structure.Units, $"/licencas/{licenseId}/estrutura")
        ];
    }

    private static ReportQualityIndicatorViewModel Quality(string key, string label, string description, int value, int total, string url)
    {
        var percentage = Percentage(value, total);
        return new ReportQualityIndicatorViewModel
        {
            Key = key,
            Label = label,
            Description = description,
            Value = value,
            Total = total,
            Percentage = percentage,
            Tone = percentage >= 80 ? "good" : percentage >= 55 ? "attention" : "critical",
            TargetUrl = url
        };
    }

    private static List<ReportAttentionItemViewModel> BuildAttentionItems(Guid licenseId, LicenseReportsViewModel report)
    {
        var items = new List<ReportAttentionItemViewModel>();
        AddAttention(items, "account", "Moradores sem acesso ao app",
            "A conta ainda não foi criada ou o convite de cadastro não foi concluído.",
            Math.Max(0, report.Residents.Active - report.Residents.AccountsCreated), "warning", $"/licencas/{licenseId}/estrutura");
        AddAttention(items, "device", "Contas sem aplicativo vinculado",
            "A conta existe, mas nenhum dispositivo ativo foi identificado.",
            Math.Max(0, report.Residents.AccountsCreated - report.Residents.AppLinked), "info", $"/licencas/{licenseId}/estrutura");
        AddAttention(items, "credential", "Moradores sem credencial ativa",
            "Cadastros ativos que ainda não possuem uma credencial válida.",
            Math.Max(0, report.Residents.Active - report.Residents.WithActiveCredential), "warning", $"/licencas/{licenseId}/credenciais");
        AddAttention(items, "vacancy", "Unidades sem morador vinculado",
            "Unidades cadastradas que ainda não possuem ocupação ativa registrada.",
            report.Structure.VacantUnits, "info", $"/licencas/{licenseId}/estrutura");
        AddAttention(items, "denied", "Acessos negados no período",
            "Eventos que merecem revisão de credencial, rota ou horário permitido.",
            report.Operation.DeniedAccesses, report.Operation.DeniedAccesses > 10 ? "critical" : "warning", $"/licencas/{licenseId}/credenciais");

        return items.OrderBy(x => x.Severity == "critical" ? 0 : x.Severity == "warning" ? 1 : 2)
            .ThenByDescending(x => x.Count)
            .ToList();
    }

    private static void AddAttention(List<ReportAttentionItemViewModel> items, string key, string title, string description,
        int count, string severity, string targetUrl)
    {
        if (count <= 0) return;
        items.Add(new ReportAttentionItemViewModel
        {
            Key = key,
            Title = title,
            Description = description,
            Count = count,
            Severity = severity,
            TargetUrl = targetUrl
        });
    }

    private static List<ReportBlockAdoptionViewModel> BuildBlockAdoption(
        IReadOnlyCollection<UnitRow> units,
        IReadOnlyCollection<ResidentRow> residents,
        IReadOnlySet<Guid> appLinked)
    {
        return units.GroupBy(x => new { x.BlockId, x.BlockName })
            .Select(block =>
            {
                var blockUnitIds = block.Select(x => x.Id).ToHashSet();
                var blockResidents = residents.Where(x => blockUnitIds.Contains(x.UnitId)).ToList();
                var accounts = blockResidents.Count(x => x.HasAccount);
                return new ReportBlockAdoptionViewModel
                {
                    BlockId = block.Key.BlockId,
                    BlockName = block.Key.BlockName,
                    Units = block.Count(),
                    OccupiedUnits = blockResidents.Select(x => x.UnitId).Distinct().Count(),
                    Residents = blockResidents.Count,
                    AccountsCreated = accounts,
                    AppLinked = blockResidents.Count(x => appLinked.Contains(x.Id)),
                    AdoptionRate = Percentage(accounts, blockResidents.Count)
                };
            })
            .OrderByDescending(x => x.Residents)
            .ThenBy(x => x.BlockName)
            .ToList();
    }

    private static List<ReportDistributionItemViewModel> BuildResidentDistribution(IReadOnlyCollection<ResidentRow> residents)
    {
        var labels = new Dictionary<ResidentAccessTypeEnum, string>
        {
            [ResidentAccessTypeEnum.Default] = "Moradores",
            [ResidentAccessTypeEnum.Responsible] = "Responsáveis",
            [ResidentAccessTypeEnum.NonResponsible] = "Dependentes"
        };
        return residents.GroupBy(x => x.Type)
            .Select(group => new ReportDistributionItemViewModel
            {
                Key = group.Key.ToString(),
                Label = labels.GetValueOrDefault(group.Key, "Moradores"),
                Count = group.Count(),
                Percentage = Percentage(group.Count(), residents.Count)
            })
            .OrderByDescending(x => x.Count)
            .ToList();
    }

    private static List<ReportDistributionItemViewModel> BuildCredentialDistribution(IReadOnlyCollection<CredentialRow> credentials)
    {
        var labels = new Dictionary<AccessCredentialTypeEnum, string>
        {
            [AccessCredentialTypeEnum.Face] = "Facial",
            [AccessCredentialTypeEnum.QrCode] = "QR Code",
            [AccessCredentialTypeEnum.Card] = "Cartão",
            [AccessCredentialTypeEnum.Tag] = "Tag",
            [AccessCredentialTypeEnum.VehicleTag] = "Tag veicular",
            [AccessCredentialTypeEnum.Password] = "Senha"
        };
        return credentials.GroupBy(x => x.Type)
            .Select(group => new ReportDistributionItemViewModel
            {
                Key = group.Key.ToString(),
                Label = labels.GetValueOrDefault(group.Key, group.Key.ToString()),
                Count = group.Count(),
                Percentage = Percentage(group.Count(), credentials.Count)
            })
            .OrderByDescending(x => x.Count)
            .ToList();
    }

    private static List<ReportTrendPointViewModel> BuildTrend(
        DateTime since,
        DateTime now,
        int days,
        IEnumerable<AccessDayRow> accessRows,
        IEnumerable<VisitDayRow> visitRows)
    {
        var access = accessRows.ToDictionary(
            x => new DateTime(x.Year, x.Month, x.Day, 0, 0, 0, DateTimeKind.Utc),
            x => (x.Authorized, x.Denied));
        var visits = visitRows.ToDictionary(
            x => new DateTime(x.Year, x.Month, x.Day, 0, 0, 0, DateTimeKind.Utc),
            x => x.Count);
        var bucketDays = days <= 30 ? 1 : days <= 90 ? 3 : days <= 180 ? 7 : 14;
        var output = new List<ReportTrendPointViewModel>();

        for (var start = since.Date; start <= now.Date; start = start.AddDays(bucketDays))
        {
            var end = start.AddDays(bucketDays - 1) > now.Date ? now.Date : start.AddDays(bucketDays - 1);
            var point = new ReportTrendPointViewModel { Date = start, EndDate = end };
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                if (access.TryGetValue(date, out var accessValue))
                {
                    point.Authorized += accessValue.Item1;
                    point.Denied += accessValue.Item2;
                }
                if (visits.TryGetValue(date, out var visitValue)) point.Visitors += visitValue;
            }
            output.Add(point);
        }

        return output;
    }

    internal sealed record UnitRow(Guid Id, Guid BlockId, string BlockName);
    internal sealed record ResidentRow(Guid Id, Guid UnitId, ResidentAccessTypeEnum Type, bool IsActive,
        bool HasEmail, bool HasPhone, bool HasDocument, bool HasPhoto, bool HasAccount);
    internal sealed record CredentialRow(Guid ResidentId, AccessCredentialTypeEnum Type);
    internal sealed record AccessDayRow(int Year, int Month, int Day, int Authorized, int Denied);
    internal sealed record VisitDayRow(int Year, int Month, int Day, int Count);
}
