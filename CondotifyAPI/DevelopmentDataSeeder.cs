using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Location;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Models.Users;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI;

public static class DevelopmentDataSeeder
{
    public const string TestEmail = "teste@condotify.local";
    public const string TestPassword = "Teste@123";

    private static readonly Guid EnterpriseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LicenseId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BlockId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid UnitId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ResidentId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public static async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<DatabaseContext>();
        var hasher = services.GetRequiredService<IPasswordHasher<UserAccess>>();

        var now = DateTime.UtcNow;

        var enterprise = await context.Enterprises.FirstOrDefaultAsync(x => x.Id == EnterpriseId);
        if (enterprise == null)
        {
            enterprise = new EnterpriseDTO
            {
                Id = EnterpriseId,
                Name = "Condominio Condotify Demo",
                CNPJ = "00.000.000/0001-00",
                StateRegistration = "ISENTO",
                MunicipalRegistration = "ISENTO",
                Email = "demo@condotify.local",
                Phone = "(11) 3000-0000",
                Mobile = "(11) 90000-0000",
                Website = "https://condotify.local",
                Street = "Rua Demo",
                Number = "100",
                Complement = "Portaria",
                Neighborhood = "Centro",
                City = "Sao Paulo",
                State = "SP",
                PostalCode = "01000-000",
                Country = "Brasil",
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true,
                ContactPerson = "Administrador Demo",
                ContactEmail = TestEmail,
                ContactPhone = "(11) 90000-0000",
                LogoUrl = string.Empty,
                Notes = "Dados criados automaticamente para desenvolvimento.",
                Users = new(),
                Licenses = new()
            };

            context.Enterprises.Add(enterprise);
        }

        if (!await context.Users.AnyAsync(x => x.Email == TestEmail))
        {
            var user = new UserAccessDTO
            {
                Id = UserId,
                Name = "Usuario Teste",
                Email = TestEmail,
                PhoneNumber = "(11) 99999-9999",
                CPF = "000.000.000-00",
                RG = "00.000.000-0",
                BirthDate = "1990-01-01",
                AccessType = AccessTypeEnum.Admin,
                FirstAccess = false,
                LastAccess = now,
                CreatedAt = now,
                EnterpriseId = EnterpriseId,
                Audit = new()
            };
            user.SetPasswordHash(hasher.HashPassword(null!, TestPassword));
            context.Users.Add(user);
        }

        if (!await context.Licenses.AnyAsync(x => x.Id == LicenseId))
        {
            context.Licenses.Add(new LicenseDTO
            {
                Id = LicenseId,
                Name = "Condominio Demo",
                CNPJ = enterprise.CNPJ,
                City = enterprise.City,
                Country = enterprise.Country,
                Code = "DEMO-001",
                Organization = OrganizationTypeEnum.Residential,
                Building = BuildingTypeEnum.Vertical,
                Type = LicenseTypeEnum.Demo,
                Location = new LocationDTO
                {
                    Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    X = -23.55052f,
                    Y = -46.633308f
                },
                ExpireDate = now.AddYears(1),
                CreatedAt = now,
                EnterpriseId = EnterpriseId,
                Blocks = new(),
                Devices = new(),
                CFTVDevices = new(),
                Deliveries = new(),
                Tickets = new()
            });
        }

        if (!await context.Blocks.AnyAsync(x => x.Id == BlockId))
        {
            context.Blocks.Add(new BlockDTO
            {
                Id = BlockId,
                LicenseId = LicenseId,
                Name = "Bloco A",
                CreatedAt = now,
                LastUpdatedAt = now,
                Units = new()
            });
        }

        if (!await context.Units.AnyAsync(x => x.Id == UnitId))
        {
            context.Units.Add(new UnitDTO
            {
                Id = UnitId,
                BlockId = BlockId,
                Number = "101",
                Floor = "1",
                Residents = new()
            });
        }

        if (!await context.Residents.AnyAsync(x => x.Id == ResidentId))
        {
            context.Residents.Add(new ResidentAccessDTO
            {
                Id = ResidentId,
                UnitId = UnitId,
                Name = "Morador Demo",
                Email = "morador.demo@condotify.local",
                Password = string.Empty,
                PhoneNumber = "(11) 98888-8888",
                CPF = "111.111.111-11",
                RG = "11.111.111-1",
                BirthDate = "1992-01-01",
                ApartmentNumber = "101",
                ImgUrl = string.Empty,
                AccessType = ResidentAccessTypeEnum.Responsible,
                AccessCredentials = new List<ResidentAccessCredentialDTO>(),
                FirstAccess = true,
                Temporary = false,
                Expire = now.AddYears(10),
                LastAccess = now,
                CreatedAt = now
            });
        }

        var orphanDemoLicenses = await context.Licenses
            .Where(x => x.EnterpriseId == Guid.Empty)
            .ToListAsync();

        foreach (var license in orphanDemoLicenses)
            license.EnterpriseId = EnterpriseId;

        if (!await context.LicenseUserAccesses.AnyAsync(x => x.LicenseId == LicenseId && x.UserId == UserId))
        {
            context.LicenseUserAccesses.Add(new LicenseUserAccessDTO
            {
                Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                LicenseId = LicenseId,
                UserId = UserId,
                Role = LicenseAccessRoleEnum.Administrator,
                Permissions = LicensePermissionEnum.All,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!await context.LicenseCredentialPolicies.AnyAsync(x => x.LicenseId == LicenseId))
            context.LicenseCredentialPolicies.Add(new LicenseCredentialPolicyDTO { LicenseId = LicenseId, UpdatedAt = DateTime.UtcNow });

        await context.SaveChangesAsync();
    }
}
