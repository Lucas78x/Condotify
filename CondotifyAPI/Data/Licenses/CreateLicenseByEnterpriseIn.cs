using CondotifyAPI.Commands.Licenses;
using CondotifyAPI.Domain.Models;

namespace DigitalWorldOnline.Management.Api.Data
{
    public class CreateLicenseByEnterpriseIn
    {
        public string EnterpriseId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CNPJ { get; set; } = string.Empty;
        public OrganizationTypeEnum Organization { get; set; }
        public BuildingTypeEnum Building { get; set; }
        public LicenseTypeEnum Type { get; set; }
        public Location Location { get; set; } = new();
        public DateTime ExpireDate { get; set; }
    }

    public static class CreateLicenseByEnterpriseInConverter
    {
        public static CreateLicenseByEnterpriseCommand ToCommand(this CreateLicenseByEnterpriseIn license)
        {
            return new CreateLicenseByEnterpriseCommand(
                Guid.Parse(license.EnterpriseId),
                license.Name,
                license.CNPJ,
                license.Organization,
                license.Building,
                license.Type,
                license.Location,
                license.ExpireDate
            );
        }
    }
}
