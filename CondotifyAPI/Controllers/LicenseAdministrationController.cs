using System.Security.Claims;
using CondotifyAPI.Data.Administration;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Models.Users;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/administration")]
public sealed class LicenseAdministrationController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly ILicenseAuthorizationService _authorization;
    private readonly IPasswordHasher<UserAccess> _hasher;

    public LicenseAdministrationController(DatabaseContext context, ILicenseAuthorizationService authorization, IPasswordHasher<UserAccess> hasher)
    {
        _context = context;
        _authorization = authorization;
        _hasher = hasher;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid licenseId)
    {
        var grant = await _authorization.GetGrantAsync(User, licenseId);
        if (grant is null) return Forbid();

        var users = grant.Has(LicensePermissionEnum.ViewUsers)
            ? await _context.LicenseUserAccesses.AsNoTracking().Include(x => x.User)
                .Where(x => x.LicenseId == licenseId).OrderBy(x => x.User.Name).Select(x => ToUser(x)).ToListAsync()
            : [];
        var policy = await GetPolicyAsync(licenseId);
        return Ok(new LicenseAdministrationOut
        {
            CurrentAccess = new CurrentLicenseAccessOut { Role = grant.Role.ToString(), Permissions = (long)grant.Permissions, IsEnterpriseAdministrator = grant.IsEnterpriseAdministrator },
            Users = users,
            Policy = ToPolicy(policy)
        });
    }

    [HttpPost("users")]
    [RequireLicensePermission(LicensePermissionEnum.ManageUsers)]
    public async Task<IActionResult> CreateUser(Guid licenseId, [FromBody] CreateLicenseUserIn input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { Errors = "Informe o nome do usuario." });
        if (string.IsNullOrWhiteSpace(input.Email) || !input.Email.Contains('@')) return BadRequest(new { Errors = "Informe um e-mail valido." });
        if (string.IsNullOrWhiteSpace(input.Password) || input.Password.Length < 8) return BadRequest(new { Errors = "A senha temporaria deve ter pelo menos 8 caracteres." });
        var enterpriseId = await _context.Licenses.Where(x => x.Id == licenseId).Select(x => x.EnterpriseId).FirstAsync();
        var email = input.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == email);
        if (user is not null && user.EnterpriseId != enterpriseId) return Conflict(new { Errors = "Este e-mail pertence a outra empresa." });

        if (user is null)
        {
            user = new UserAccessDTO
            {
                Id = Guid.NewGuid(), Name = input.Name.Trim(), Email = email, PhoneNumber = input.PhoneNumber.Trim(),
                CPF = string.Empty, RG = string.Empty, BirthDate = string.Empty, AccessType = AccessTypeEnum.Viewer,
                FirstAccess = true, LastAccess = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, EnterpriseId = enterpriseId, Audit = []
            };
            user.SetPasswordHash(_hasher.HashPassword(null!, input.Password));
            _context.Users.Add(user);
        }

        if (await _context.LicenseUserAccesses.AnyAsync(x => x.LicenseId == licenseId && x.UserId == user.Id))
            return Conflict(new { Errors = "Este usuario ja esta vinculado ao condominio." });

        var permissions = LicenseAccessDefaults.Normalize(input.Permissions is > 0 ? (LicensePermissionEnum)input.Permissions.Value : LicenseAccessDefaults.ForRole(input.Role));
        var access = new LicenseUserAccessDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, UserId = user.Id, Role = input.Role,
            Permissions = permissions, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _context.LicenseUserAccesses.Add(access);
        await _context.SaveChangesAsync();
        access.User = user;
        return Created("", ToUser(access));
    }

    [HttpPatch("users/{accessId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageUsers)]
    public async Task<IActionResult> UpdateUser(Guid licenseId, Guid accessId, [FromBody] UpdateLicenseUserIn input)
    {
        var access = await _context.LicenseUserAccesses.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == accessId && x.LicenseId == licenseId);
        if (access is null) return NotFound();
        if ((input.Permissions & ~(long)LicensePermissionEnum.All) != 0) return BadRequest(new { Errors = "A lista de permissoes contem valores invalidos." });

        var permissions = LicenseAccessDefaults.Normalize(input.Permissions == 0 ? LicenseAccessDefaults.ForRole(input.Role) : (LicensePermissionEnum)input.Permissions);
        if (access.UserId == CurrentUserId() && (!input.IsActive || !permissions.HasFlag(LicensePermissionEnum.ManageUsers)))
            return BadRequest(new { Errors = "Voce nao pode remover o proprio acesso de administracao de usuarios." });

        access.Role = input.Role;
        access.Permissions = permissions;
        access.IsActive = input.IsActive;
        access.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(ToUser(access));
    }

    [HttpPatch("credential-policy")]
    [RequireLicensePermission(LicensePermissionEnum.ManageSettings)]
    public async Task<IActionResult> UpdatePolicy(Guid licenseId, [FromBody] UpdateCredentialPolicyIn input)
    {
        var validation = ValidatePolicy(input);
        if (validation is not null) return BadRequest(new { Errors = validation });
        var policy = await GetPolicyAsync(licenseId);
        policy.QrCodeValidityMinutes = input.QrCodeValidityMinutes;
        policy.AllowQrCodeRenewal = input.AllowQrCodeRenewal;
        policy.MaxQrCodeRenewals = input.AllowQrCodeRenewal ? input.MaxQrCodeRenewals : 0;
        policy.QrCodeRenewalMinutes = input.QrCodeRenewalMinutes;
        policy.TemporaryFaceValidityMinutes = input.TemporaryFaceValidityMinutes;
        policy.MaxTemporaryFaceValidityMinutes = input.MaxTemporaryFaceValidityMinutes;
        policy.RequireFacePhoto = input.RequireFacePhoto;
        policy.AutoDeactivateExpiredCredentials = input.AutoDeactivateExpiredCredentials;
        policy.RemoveExpiredCredentialsFromDevices = input.RemoveExpiredCredentialsFromDevices;
        policy.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(ToPolicy(policy));
    }

    private async Task<LicenseCredentialPolicyDTO> GetPolicyAsync(Guid licenseId)
    {
        var policy = await _context.LicenseCredentialPolicies.FirstOrDefaultAsync(x => x.LicenseId == licenseId);
        if (policy is not null) return policy;
        policy = new LicenseCredentialPolicyDTO { LicenseId = licenseId, UpdatedAt = DateTime.UtcNow };
        _context.LicenseCredentialPolicies.Add(policy);
        await _context.SaveChangesAsync();
        return policy;
    }

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private static LicenseUserAccessOut ToUser(LicenseUserAccessDTO item) => new() { Id = item.Id, UserId = item.UserId, Name = item.User.Name, Email = item.User.Email, PhoneNumber = item.User.PhoneNumber, Role = item.Role.ToString(), Permissions = (long)item.Permissions, IsActive = item.IsActive, LastAccess = item.User.LastAccess };
    private static CredentialPolicyOut ToPolicy(LicenseCredentialPolicyDTO item) => new() { QrCodeValidityMinutes = item.QrCodeValidityMinutes, AllowQrCodeRenewal = item.AllowQrCodeRenewal, MaxQrCodeRenewals = item.MaxQrCodeRenewals, QrCodeRenewalMinutes = item.QrCodeRenewalMinutes, TemporaryFaceValidityMinutes = item.TemporaryFaceValidityMinutes, MaxTemporaryFaceValidityMinutes = item.MaxTemporaryFaceValidityMinutes, RequireFacePhoto = item.RequireFacePhoto, AutoDeactivateExpiredCredentials = item.AutoDeactivateExpiredCredentials, RemoveExpiredCredentialsFromDevices = item.RemoveExpiredCredentialsFromDevices };
    private static string? ValidatePolicy(UpdateCredentialPolicyIn input)
    {
        if (input.QrCodeValidityMinutes is < 5 or > 43200) return "A validade do QR Code deve ficar entre 5 minutos e 30 dias.";
        if (input.MaxQrCodeRenewals is < 0 or > 50) return "O limite de renovacoes deve ficar entre 0 e 50.";
        if (input.QrCodeRenewalMinutes is < 5 or > 43200) return "O periodo de renovacao deve ficar entre 5 minutos e 30 dias.";
        if (input.TemporaryFaceValidityMinutes is < 5 or > 43200) return "A validade facial temporaria deve ficar entre 5 minutos e 30 dias.";
        if (input.MaxTemporaryFaceValidityMinutes < input.TemporaryFaceValidityMinutes || input.MaxTemporaryFaceValidityMinutes > 525600) return "O limite facial deve ser maior que a validade padrao e menor que um ano.";
        return null;
    }
}
