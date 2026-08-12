using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using CondotifyAPI.Data.Imports;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/imports")]
public sealed class StructureImportsController(
    DatabaseContext context,
    StructureImportCsvParser parser) : ControllerBase
{
    [HttpPost("structure/preview")]
    [RequireLicensePermission(LicensePermissionEnum.ManageStructure)]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> Preview(Guid licenseId, [FromBody] StructureImportIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var policyErrors = AssistedMigrationPolicy.Validate(input);
        if (policyErrors.Count > 0)
            return BadRequest(new { Result = "MigrationPolicyRejected", Errors = policyErrors });
        var prepared = await PrepareAsync(licenseId, input.Content);
        prepared.Preview.PreviewId = Guid.NewGuid();
        prepared.Preview.FileSha256 = AssistedMigrationPolicy.FileSha256(input.Content);
        var actor = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Migração";
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = "AssistedMigration",
            EntityId = prepared.Preview.PreviewId,
            Action = "Previewed",
            Status = prepared.Preview.CanExecute ? "Success" : "Pending",
            Summary = $"Prévia de migração analisada: {prepared.Preview.ValidRows} registro(s) válido(s) e {prepared.Preview.InvalidRows} pendência(s).",
            DetailsJson = JsonSerializer.Serialize(new
            {
                FileName = SafeFileName(input.FileName),
                prepared.Preview.FileSha256,
                SourceSystem = AssistedMigrationPolicy.SourceLabel(input.SourceSystem),
                ProcessingBasis = AssistedMigrationPolicy.BasisLabel(input.ProcessingBasis),
                AuthorizedBy = input.AuthorizedBy.Trim(),
                AuthorizationReference = input.AuthorizationReference.Trim(),
                ContentPersisted = false
            }),
            UserName = actor,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(prepared.Preview);
    }

    [HttpPost("structure/execute")]
    [RequireLicensePermission(LicensePermissionEnum.ManageStructure)]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> Execute(Guid licenseId, [FromBody] StructureImportIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var policyErrors = AssistedMigrationPolicy.Validate(input);
        if (policyErrors.Count > 0)
            return BadRequest(new { Result = "MigrationPolicyRejected", Errors = policyErrors });
        var fileSha256 = AssistedMigrationPolicy.FileSha256(input.Content);
        if (!input.PreviewId.HasValue ||
            !string.Equals(input.PreviewFileSha256, fileSha256, StringComparison.OrdinalIgnoreCase))
            return Conflict(new
            {
                Result = "MigrationPreviewExpired",
                Errors = "O arquivo não corresponde à prévia aprovada. Gere uma nova prévia antes de executar a migração."
            });
        var previewCutoff = DateTime.UtcNow.AddMinutes(-30);
        var previewAudit = await context.AccessOperationAudits.AsNoTracking().FirstOrDefaultAsync(x =>
            x.LicenseId == licenseId &&
            x.EntityType == "AssistedMigration" &&
            x.EntityId == input.PreviewId &&
            x.Action == "Previewed" &&
            x.Status == "Success" &&
            x.CreatedAt >= previewCutoff);
        if (previewAudit is null || !AuditMatchesPreview(previewAudit.DetailsJson, fileSha256, input))
            return Conflict(new
            {
                Result = "MigrationPreviewExpired",
                Errors = "A prévia não foi encontrada, possui pendências ou expirou. Gere uma nova análise antes de executar."
            });
        var key = input.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 180)
            return BadRequest(new { Result = "InvalidIdempotencyKey", Errors = "A chave de confirmacao da importacao e invalida." });

        var previous = await context.AccessBatchOperations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.IdempotencyKey == key);
        if (previous is not null)
            return Conflict(new
            {
                Result = "ImportAlreadyProcessed",
                Errors = "Esta importacao ja foi processada. Recarregue a estrutura antes de enviar novamente.",
                ImportId = previous.Id
            });

        var prepared = await PrepareAsync(licenseId, input.Content);
        if (!prepared.Preview.CanExecute)
            return UnprocessableEntity(prepared.Preview);

        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;
            var blockLookup = await context.Blocks
                .Include(x => x.Units)
                .Where(x => x.LicenseId == licenseId)
                .ToDictionaryAsync(x => StructureImportCsvParser.NormalizeKey(x.Name), StringComparer.OrdinalIgnoreCase);
            var unitLookup = blockLookup.Values
                .SelectMany(x => x.Units.Select(unit => new { Block = x, Unit = unit }))
                .ToDictionary(
                    x => UnitKey(x.Block.Name, x.Unit.Number),
                    x => x.Unit,
                    StringComparer.OrdinalIgnoreCase);

            var createdBlocks = 0;
            var createdUnits = 0;
            var createdPeople = 0;
            var createdVehicles = 0;

            foreach (var item in prepared.Rows)
            {
                var blockKey = StructureImportCsvParser.NormalizeKey(item.Row.Block);
                if (!blockLookup.TryGetValue(blockKey, out var block))
                {
                    block = new BlockDTO
                    {
                        Id = Guid.NewGuid(),
                        LicenseId = licenseId,
                        Name = item.Row.Block.Trim(),
                        CreatedAt = now,
                        LastUpdatedAt = now
                    };
                    context.Blocks.Add(block);
                    blockLookup[blockKey] = block;
                    createdBlocks++;
                }

                var unitKey = UnitKey(item.Row.Block, item.Row.Unit);
                if (!unitLookup.TryGetValue(unitKey, out var unit))
                {
                    unit = new UnitDTO
                    {
                        Id = Guid.NewGuid(),
                        BlockId = block.Id,
                        Block = block,
                        Number = item.Row.Unit.Trim(),
                        Floor = item.Row.Floor.Trim()
                    };
                    context.Units.Add(unit);
                    unitLookup[unitKey] = unit;
                    createdUnits++;
                }

                ResidentAccessDTO? resident = null;
                if (!string.IsNullOrWhiteSpace(item.Row.Name))
                {
                    resident = new ResidentAccessDTO
                    {
                        Id = Guid.NewGuid(),
                        UnitId = unit.Id,
                        Unit = unit,
                        Name = item.Row.Name.Trim(),
                        Email = item.Row.Email.Trim(),
                        PhoneNumber = item.Row.Phone.Trim(),
                        CPF = item.Row.CPF,
                        RG = item.Row.RG.Trim(),
                        ApartmentNumber = unit.Number,
                        Description = string.Empty,
                        AccessType = item.Category,
                        FirstAccess = true,
                        NotifyAccess = false,
                        IsActive = true,
                        Temporary = false,
                        Expire = now.AddYears(10),
                        LastAccess = now,
                        CreatedAt = now
                    };
                    context.Residents.Add(resident);
                    context.ResidentUnitLinks.Add(new ResidentUnitLinkDTO
                    {
                        Id = Guid.NewGuid(),
                        ResidentId = resident.Id,
                        Resident = resident,
                        UnitId = unit.Id,
                        Unit = unit,
                        Relationship = item.Relationship,
                        IsPrimary = true,
                        IsActive = true,
                        StartsAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    createdPeople++;
                }

                if (!string.IsNullOrWhiteSpace(item.Row.Plate))
                {
                    var vehicle = new VehicleDTO
                    {
                        Id = Guid.NewGuid(),
                        UnitId = unit.Id,
                        Unit = unit,
                        ResidentId = resident?.Id,
                        Resident = resident,
                        Plate = item.Row.Plate,
                        Brand = item.Row.VehicleBrand.Trim(),
                        Model = item.Row.VehicleModel.Trim(),
                        Color = item.Row.VehicleColor.Trim(),
                        Type = string.IsNullOrWhiteSpace(item.Row.VehicleType) ? "Carro" : item.Row.VehicleType.Trim(),
                        TagIdentifier = item.Row.Tag.Trim(),
                        IsActive = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    context.Vehicles.Add(vehicle);
                    createdVehicles++;

                    if (resident is not null && !string.IsNullOrWhiteSpace(vehicle.TagIdentifier))
                    {
                        context.ResidentAccessCredentials.Add(new ResidentAccessCredentialDTO
                        {
                            Id = Guid.NewGuid(),
                            ResidentId = resident.Id,
                            Resident = resident,
                            CredentialType = AccessCredentialTypeEnum.VehicleTag,
                            Identifier = vehicle.TagIdentifier,
                            IsActive = true,
                            ValidFrom = now,
                            ValidTo = now.AddYears(10),
                            CreatedAt = now
                        });
                    }
                }
            }

            var actor = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Importacao";
            var import = new AccessBatchOperationDTO
            {
                Id = Guid.NewGuid(),
                LicenseId = licenseId,
                Operation = "StructureImport",
                IdempotencyKey = key,
                Status = AccessBatchStatusEnum.Completed,
                TotalItems = prepared.Rows.Count,
                ProcessedItems = prepared.Rows.Count,
                SuccessfulItems = prepared.Rows.Count,
                RequestedBy = actor,
                FilterJson = JsonSerializer.Serialize(new
                {
                    FileName = SafeFileName(input.FileName),
                    FileSha256 = fileSha256,
                    input.PreviewId,
                    SourceSystem = AssistedMigrationPolicy.SourceLabel(input.SourceSystem),
                    ProcessingBasis = AssistedMigrationPolicy.BasisLabel(input.ProcessingBasis),
                    AuthorizedBy = input.AuthorizedBy.Trim(),
                    AuthorizationReference = input.AuthorizationReference.Trim(),
                    ControllerAuthorizationConfirmed = input.ControllerAuthorizationConfirmed,
                    PurposeLimitationConfirmed = input.PurposeLimitationConfirmed,
                    NoRestrictedDataConfirmed = input.NoRestrictedDataConfirmed,
                    createdBlocks,
                    createdUnits,
                    createdPeople,
                    createdVehicles
                }),
                Priority = 50,
                CreatedAt = now,
                StartedAt = now,
                FinishedAt = now
            };
            context.AccessBatchOperations.Add(import);
            context.AccessOperationAudits.Add(new AccessOperationAuditDTO
            {
                Id = Guid.NewGuid(),
                LicenseId = licenseId,
                EntityType = "AssistedMigration",
                EntityId = import.Id,
                Action = "Completed",
                Status = "Success",
                Summary = $"Migração assistida concluída: {createdBlocks} agrupamento(s), {createdUnits} unidade(s), {createdPeople} pessoa(s) e {createdVehicles} veículo(s).",
                DetailsJson = import.FilterJson,
                UserName = actor,
                CreatedAt = now
            });

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Ok(new StructureImportExecutionOut
            {
                ImportId = import.Id,
                CreatedBlocks = createdBlocks,
                CreatedUnits = createdUnits,
                CreatedPeople = createdPeople,
                CreatedVehicles = createdVehicles,
                Message = "Importacao concluida com sucesso."
            });
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return Conflict(new
            {
                Result = "ImportConflict",
                Errors = "Os dados mudaram depois da pre-validacao. Gere uma nova previa antes de tentar novamente."
            });
        }
    }

    private async Task<PreparedImport> PrepareAsync(Guid licenseId, string content)
    {
        var parsed = parser.Parse(content);
        var preview = new StructureImportPreviewOut { Errors = parsed.Errors.ToList() };
        if (parsed.Errors.Count > 0)
        {
            preview.InvalidRows = parsed.Rows.Count;
            preview.TotalRows = parsed.Rows.Count;
            return new PreparedImport(preview, []);
        }

        var blocks = await context.Blocks.AsNoTracking()
            .Include(x => x.Units).ThenInclude(x => x.Residents)
            .Include(x => x.Units).ThenInclude(x => x.Vehicles)
            .Where(x => x.LicenseId == licenseId)
            .AsSplitQuery()
            .ToListAsync();

        var existingBlocks = blocks.Select(x => StructureImportCsvParser.NormalizeKey(x.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingUnits = blocks.SelectMany(block => block.Units.Select(unit => UnitKey(block.Name, unit.Number)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingPeople = blocks.SelectMany(block => block.Units.SelectMany(unit =>
                unit.Residents.Select(x => PersonKey(block.Name, unit.Number, PersonIdentity(x.Name, x.CPF)))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingVehicles = blocks.SelectMany(block => block.Units.SelectMany(unit =>
                unit.Vehicles.Select(x => VehicleKey(block.Name, unit.Number, x.Plate))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var simulatedBlocks = new HashSet<string>(existingBlocks, StringComparer.OrdinalIgnoreCase);
        var simulatedUnits = new HashSet<string>(existingUnits, StringComparer.OrdinalIgnoreCase);
        var simulatedPeople = new HashSet<string>(existingPeople, StringComparer.OrdinalIgnoreCase);
        var simulatedVehicles = new HashSet<string>(existingVehicles, StringComparer.OrdinalIgnoreCase);
        var rows = new List<PreparedRow>();

        foreach (var row in parsed.Rows)
        {
            var messages = row.Errors.ToList();
            ValidateLengths(row, messages);
            if (string.IsNullOrWhiteSpace(row.Block)) messages.Add("Informe o agrupamento.");
            if (string.IsNullOrWhiteSpace(row.Unit)) messages.Add("Informe a unidade.");
            if (string.IsNullOrWhiteSpace(row.Name) && HasPersonData(row)) messages.Add("Informe o nome da pessoa.");
            if (!string.IsNullOrWhiteSpace(row.CPF) && row.CPF.Length != 11) messages.Add("O CPF deve possuir 11 digitos.");
            if (!string.IsNullOrWhiteSpace(row.Email) && !MailAddress.TryCreate(row.Email, out _)) messages.Add("O e-mail e invalido.");
            if (!string.IsNullOrWhiteSpace(row.Tag) && string.IsNullOrWhiteSpace(row.Plate)) messages.Add("Informe a placa para vincular a TAG.");
            if (!string.IsNullOrWhiteSpace(row.Tag) && string.IsNullOrWhiteSpace(row.Name)) messages.Add("Informe a pessoa responsavel pela TAG.");

            var category = ParseCategory(row.Category, messages);
            var relationship = ParseRelationship(row.Relationship, category, messages);
            var blockKey = StructureImportCsvParser.NormalizeKey(row.Block);
            var unitKey = UnitKey(row.Block, row.Unit);

            if (!string.IsNullOrWhiteSpace(row.Name) && !string.IsNullOrWhiteSpace(row.Unit))
            {
                var identity = PersonIdentity(row.Name, row.CPF);
                var personKey = PersonKey(row.Block, row.Unit, identity);
                if (!simulatedPeople.Add(personKey))
                    messages.Add(string.IsNullOrWhiteSpace(row.CPF)
                        ? "Já existe uma pessoa com este nome na unidade. Revise para evitar duplicidade."
                        : "Já existe uma pessoa com este CPF na unidade.");
            }
            if (!string.IsNullOrWhiteSpace(row.Plate) && !string.IsNullOrWhiteSpace(row.Unit))
            {
                var vehicleKey = VehicleKey(row.Block, row.Unit, row.Plate);
                if (!simulatedVehicles.Add(vehicleKey)) messages.Add("Ja existe um veiculo com esta placa na unidade.");
            }

            var isValid = messages.Count == 0;
            if (isValid)
            {
                if (simulatedBlocks.Add(blockKey)) preview.NewBlocks++;
                if (simulatedUnits.Add(unitKey)) preview.NewUnits++;
                if (!string.IsNullOrWhiteSpace(row.Name)) preview.NewPeople++;
                if (!string.IsNullOrWhiteSpace(row.Plate)) preview.NewVehicles++;
                rows.Add(new PreparedRow(row, category, relationship));
            }

            preview.Rows.Add(new StructureImportRowOut
            {
                RowNumber = row.RowNumber,
                Block = row.Block,
                Unit = row.Unit,
                PersonName = row.Name,
                VehiclePlate = row.Plate,
                IsValid = isValid,
                Messages = messages
            });
        }

        preview.TotalRows = preview.Rows.Count;
        preview.ValidRows = preview.Rows.Count(x => x.IsValid);
        preview.InvalidRows = preview.TotalRows - preview.ValidRows;
        preview.CanExecute = preview.TotalRows > 0 && preview.InvalidRows == 0 && preview.Errors.Count == 0;
        return new PreparedImport(preview, rows);
    }

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId) =>
        Guid.TryParse(User.FindFirstValue("enterprise_id"), out var enterpriseId) &&
        await context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);

    private static ResidentAccessTypeEnum ParseCategory(string value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return ResidentAccessTypeEnum.Responsible;
        return StructureImportCsvParser.NormalizeKey(value) switch
        {
            "responsavel" or "moradorresponsavel" or "proprietario" or "locatario" => ResidentAccessTypeEnum.Responsible,
            "morador" or "dependente" or "naoresponsavel" => ResidentAccessTypeEnum.NonResponsible,
            "visitante" or "convidado" => ResidentAccessTypeEnum.Guest,
            "prestador" or "prestadordeservico" or "servico" => ResidentAccessTypeEnum.ServiceProvider,
            _ => InvalidCategory(errors)
        };
    }

    private static ResidentAccessTypeEnum InvalidCategory(ICollection<string> errors)
    {
        errors.Add("Categoria invalida. Use Responsavel, Morador, Visitante ou Prestador.");
        return ResidentAccessTypeEnum.Default;
    }

    private static ResidentUnitRelationshipEnum ParseRelationship(
        string value,
        ResidentAccessTypeEnum category,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return category == ResidentAccessTypeEnum.Responsible
                ? ResidentUnitRelationshipEnum.Responsible
                : ResidentUnitRelationshipEnum.Resident;

        return StructureImportCsvParser.NormalizeKey(value) switch
        {
            "proprietario" or "proprietarioresponsavel" => ResidentUnitRelationshipEnum.OwnerResponsible,
            "locatario" or "locatarioresponsavel" => ResidentUnitRelationshipEnum.TenantResponsible,
            "responsavel" => ResidentUnitRelationshipEnum.Responsible,
            "morador" or "residente" => ResidentUnitRelationshipEnum.Resident,
            "dependente" => ResidentUnitRelationshipEnum.Dependent,
            _ => InvalidRelationship(errors)
        };
    }

    private static ResidentUnitRelationshipEnum InvalidRelationship(ICollection<string> errors)
    {
        errors.Add("Vinculo invalido. Use Proprietario, Locatario, Responsavel, Morador ou Dependente.");
        return ResidentUnitRelationshipEnum.Resident;
    }

    private static void ValidateLengths(StructureImportParsedRow row, ICollection<string> errors)
    {
        Check(row.Block, 200, "Agrupamento", errors);
        Check(row.Unit, 50, "Unidade", errors);
        Check(row.Floor, 20, "Andar", errors);
        Check(row.Name, 150, "Nome", errors);
        Check(row.Email, 150, "E-mail", errors);
        Check(row.Phone, 20, "Telefone", errors);
        Check(row.RG, 12, "RG", errors);
        Check(row.Plate, 10, "Placa", errors);
        Check(row.VehicleBrand, 60, "Marca do veiculo", errors);
        Check(row.VehicleModel, 80, "Modelo do veiculo", errors);
        Check(row.VehicleColor, 40, "Cor do veiculo", errors);
        Check(row.VehicleType, 40, "Tipo do veiculo", errors);
        Check(row.Tag, 100, "TAG", errors);
    }

    private static void Check(string value, int max, string label, ICollection<string> errors)
    {
        if (value.Length > max) errors.Add($"{label} excede {max} caracteres.");
    }

    private static bool HasPersonData(StructureImportParsedRow row) =>
        !string.IsNullOrWhiteSpace(row.CPF) ||
        !string.IsNullOrWhiteSpace(row.RG) ||
        !string.IsNullOrWhiteSpace(row.Email) ||
        !string.IsNullOrWhiteSpace(row.Phone) ||
        !string.IsNullOrWhiteSpace(row.Category) ||
        !string.IsNullOrWhiteSpace(row.Relationship);

    private static string UnitKey(string block, string unit) =>
        $"{StructureImportCsvParser.NormalizeKey(block)}|{StructureImportCsvParser.NormalizeKey(unit)}";

    private static string PersonKey(string block, string unit, string cpf) =>
        $"{UnitKey(block, unit)}|{cpf}";

    private static string PersonIdentity(string name, string cpf) =>
        string.IsNullOrWhiteSpace(cpf) ? $"nome:{StructureImportCsvParser.NormalizeKey(name)}" : $"cpf:{cpf}";

    private static bool AuditMatchesPreview(string detailsJson, string expectedSha256, StructureImportIn input)
    {
        try
        {
            using var document = JsonDocument.Parse(detailsJson);
            var root = document.RootElement;
            return JsonEquals(root, "FileSha256", expectedSha256) &&
                   JsonEquals(root, "SourceSystem", AssistedMigrationPolicy.SourceLabel(input.SourceSystem)) &&
                   JsonEquals(root, "ProcessingBasis", AssistedMigrationPolicy.BasisLabel(input.ProcessingBasis)) &&
                   JsonEquals(root, "AuthorizedBy", input.AuthorizedBy.Trim()) &&
                   JsonEquals(root, "AuthorizationReference", input.AuthorizationReference.Trim());
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool JsonEquals(JsonElement root, string property, string expected) =>
        root.TryGetProperty(property, out var value) &&
        string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private static string VehicleKey(string block, string unit, string plate) =>
        $"{UnitKey(block, unit)}|{StructureImportCsvParser.NormalizeKey(plate)}";

    private static string SafeFileName(string? fileName) =>
        Path.GetFileName(fileName ?? string.Empty).Trim() is { Length: > 180 } value ? value[..180] : Path.GetFileName(fileName ?? string.Empty).Trim();

    private sealed record PreparedImport(StructureImportPreviewOut Preview, List<PreparedRow> Rows);
    private sealed record PreparedRow(
        StructureImportParsedRow Row,
        ResidentAccessTypeEnum Category,
        ResidentUnitRelationshipEnum Relationship);
}
