
using CondotifyAPI.Commands.Enterprises;

namespace DigitalWorldOnline.Management.Api.Data;

public class CreateEnterpriseIn
{
    public string Name { get; set; } = string.Empty;
    public string CNPJ { get; set; } = string.Empty;
    public string StateRegistration { get; set; } = string.Empty;
    public string MunicipalRegistration { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;

    public string Street { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Complement { get; set; } = string.Empty;
    public string Neighborhood { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public string ContactPerson { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;

    public string LogoUrl { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public static class CreateEnterpriseInConverter
{
    public static CreateEnterpriseCommand ToCommand(this CreateEnterpriseIn enterprise)
    {
        return new CreateEnterpriseCommand(
            enterprise.Name,
            enterprise.CNPJ,
            enterprise.StateRegistration,
            enterprise.MunicipalRegistration,
            enterprise.Email,
            enterprise.Phone,
            enterprise.Mobile,
            enterprise.Website,
            enterprise.Street,
            enterprise.Number,
            enterprise.Complement,
            enterprise.Neighborhood,
            enterprise.City,
            enterprise.State,
            enterprise.PostalCode,
            enterprise.Country,
            enterprise.IsActive,
            enterprise.ContactPerson,
            enterprise.ContactEmail,
            enterprise.ContactPhone,
            enterprise.LogoUrl,
            enterprise.Notes
        );
    }
}
