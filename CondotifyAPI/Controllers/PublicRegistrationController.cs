using System.Security.Cryptography;
using System.Text;
using CondotifyAPI.Data.People;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Models.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/registration-invites")]
public class PublicRegistrationController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IPasswordHasher<ResidentAccess> _passwordHasher;

    public PublicRegistrationController(DatabaseContext context, IPasswordHasher<ResidentAccess> passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token)
    {
        var invite = await FindInviteAsync(token);
        if (invite == null) return NotFound(new { Errors = "Convite nao encontrado ou invalido." });

        var now = DateTime.UtcNow;
        if (invite.ExpiresAt <= now && invite.Status is RegistrationInviteStatusEnum.Pending or RegistrationInviteStatusEnum.Opened)
            invite.Status = RegistrationInviteStatusEnum.Expired;
        else if (invite.Status == RegistrationInviteStatusEnum.Pending)
        {
            invite.Status = RegistrationInviteStatusEnum.Opened;
            invite.OpenedAt = now;
        }

        invite.UpdatedAt = now;
        await _context.SaveChangesAsync();
        var output = ToPublicOut(invite);
        output.ExistingAccount = await HasExistingAccountAsync(invite);
        return Ok(output);
    }

    [HttpPost("{token}/complete")]
    public async Task<IActionResult> Complete(string token, [FromBody] CompleteRegistrationInviteIn input)
    {
        var invite = await FindInviteAsync(token);
        if (invite == null) return NotFound(new { Errors = "Convite nao encontrado ou invalido." });
        if (invite.Status is RegistrationInviteStatusEnum.Completed or RegistrationInviteStatusEnum.Canceled)
            return Conflict(new { Errors = "Este convite ja foi finalizado." });

        var now = DateTime.UtcNow;
        if (invite.ExpiresAt <= now)
        {
            invite.Status = RegistrationInviteStatusEnum.Expired;
            invite.UpdatedAt = now;
            await _context.SaveChangesAsync();
            return BadRequest(new { Errors = "Este convite expirou. Solicite um novo convite a administracao." });
        }
        var resident = invite.Resident;
        // O endereço originalmente convidado é a identidade à qual este token foi enviado.
        // Não permita trocar o destinatário numa página pública e vincular outra conta.
        var requestedEmail = string.IsNullOrWhiteSpace(resident.Email)
            ? input.Email?.Trim() ?? string.Empty
            : resident.Email.Trim();
        ResidentAccessDTO? existingAccount = null;
        if (!string.IsNullOrWhiteSpace(requestedEmail))
        {
            var normalizedEmail = requestedEmail.ToLowerInvariant();
            existingAccount = await _context.Residents.IgnoreQueryFilters()
                .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block).ThenInclude(x => x.License)
                .FirstOrDefaultAsync(x => x.Id != resident.Id && x.Email.ToLower() == normalizedEmail && x.Password != string.Empty);
        }

        if (existingAccount is not null)
        {
            if (!input.ConfirmExistingAccount)
                return BadRequest(new { Errors = "Confirme que deseja vincular esta unidade à sua conta existente." });
            if (!existingAccount.IsActive)
                return Conflict(new { Errors = "A conta existente está inativa. Peça à administração para revisar o acesso." });
            if (await HasOperationalDependenciesAsync(resident.Id, invite.Id))
                return Conflict(new { Errors = "Este cadastro já possui movimentações ou credenciais. Peça à administração para consolidar os vínculos antes de concluir o convite." });

            var links = await _context.ResidentUnitLinks.IgnoreQueryFilters()
                .Where(x => x.ResidentId == resident.Id).ToListAsync();
            foreach (var link in links)
            {
                if (existingAccount.UnitLinks.Any(x => x.UnitId == link.UnitId))
                    _context.ResidentUnitLinks.Remove(link);
                else
                {
                    link.ResidentId = existingAccount.Id;
                    link.Resident = existingAccount;
                }
            }
            invite.ResidentId = existingAccount.Id;
            invite.Resident = existingAccount;
            existingAccount.LastAccess = now;
            _context.Residents.Remove(resident);
            resident = existingAccount;
        }

        if (existingAccount is null)
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { Errors = "Informe o nome completo." });
            var passwordResult = ResidentPasswordSetter.Resolve(input.Password, _passwordHasher);
            if (!passwordResult.Succeeded) return BadRequest(new { Errors = passwordResult.Error });
            resident.Password = passwordResult.Hash!;
            resident.Name = input.Name!.Trim();
            resident.Email = requestedEmail;
            resident.PhoneNumber = input.PhoneNumber?.Trim() ?? resident.PhoneNumber;
            resident.CPF = input.CPF?.Trim() ?? resident.CPF;
            resident.RG = input.RG?.Trim() ?? resident.RG;
            resident.BirthDate = input.BirthDate?.Trim() ?? resident.BirthDate;
            resident.FirstAccess = false;
        }
        invite.Status = RegistrationInviteStatusEnum.Completed;
        invite.CompletedAt = now;
        invite.UpdatedAt = now;
        await _context.SaveChangesAsync();
        return Ok(ToPublicOut(invite));
    }

    private async Task<bool> HasOperationalDependenciesAsync(Guid residentId, Guid currentInviteId)
    {
        return await _context.ResidentAccessCredentials.IgnoreQueryFilters().AnyAsync(x => x.ResidentId == residentId) ||
               await _context.ResidentPasswordRecoveryTokens.IgnoreQueryFilters().AnyAsync(x => x.ResidentId == residentId) ||
               await _context.RegistrationInvites.IgnoreQueryFilters().AnyAsync(x => x.ResidentId == residentId && x.Id != currentInviteId) ||
               await _context.Vehicles.IgnoreQueryFilters().AnyAsync(x => x.ResidentId == residentId) ||
               await _context.AccessVisits.IgnoreQueryFilters().AnyAsync(x => x.HostResidentId == residentId || x.GuestResidentId == residentId) ||
               await _context.AmenityBookings.IgnoreQueryFilters().AnyAsync(x => x.ResidentId == residentId) ||
               await _context.Deliveries.IgnoreQueryFilters().AnyAsync(x => x.RecipientResidentId == residentId) ||
               await _context.AssemblyAttendances.IgnoreQueryFilters().AnyAsync(x => x.ResidentId == residentId) ||
               await _context.AssemblyVotes.IgnoreQueryFilters().AnyAsync(x => x.ResidentId == residentId) ||
               await _context.AccessRouteResidentOverrides.IgnoreQueryFilters().AnyAsync(x => x.ResidentId == residentId) ||
               await _context.FinancialReminderDeliveries.IgnoreQueryFilters().AnyAsync(x => x.ResidentId == residentId) ||
               await _context.Incidents.IgnoreQueryFilters().AnyAsync(x => x.ReportedByResidentId == residentId) ||
               await _context.IncidentAttachments.IgnoreQueryFilters().AnyAsync(x => x.UploadedByResidentId == residentId);
    }

    private async Task<bool> HasExistingAccountAsync(CondotifyAPI.Domain.DTO.Invitation.RegistrationInviteDTO invite)
    {
        var email = invite.Resident.Email.Trim().ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(email) && await _context.Residents.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.Id != invite.ResidentId && x.Email.ToLower() == email && x.Password != string.Empty);
    }

    private async Task<CondotifyAPI.Domain.DTO.Invitation.RegistrationInviteDTO?> FindInviteAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
        // IgnoreQueryFilters() deliberado: aceitar convite e AllowAnonymous (autenticado
        // pelo token na URL, nao por JWT), sem principal para popular o accessor. Consulta
        // ja restrita a um hash de token especifico -- ver Task 7 do plano de filtro de tenant.
        return await _context.RegistrationInvites.IgnoreQueryFilters()
            .Include(x => x.License)
            .Include(x => x.Resident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Resident).ThenInclude(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.TokenHash == hash);
    }

    private static PublicRegistrationInviteOut ToPublicOut(CondotifyAPI.Domain.DTO.Invitation.RegistrationInviteDTO invite) => new()
    {
        ResidentName = invite.Resident.Name,
        LicenseName = invite.License.Name,
        BlockName = ResidentLicenseScope.ResolveUnitForLicense(invite.Resident, invite.LicenseId)?.Block.Name ?? invite.Resident.Unit.Block.Name,
        UnitNumber = ResidentLicenseScope.ResolveUnitForLicense(invite.Resident, invite.LicenseId)?.Number ?? invite.Resident.Unit.Number,
        Email = invite.Resident.Email,
        PhoneNumber = invite.Resident.PhoneNumber,
        Status = invite.Status.ToString(),
        ExpiresAt = invite.ExpiresAt
    };
}
