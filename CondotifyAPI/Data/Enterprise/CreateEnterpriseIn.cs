
using CondotifyAPI.Commands.Enterprises;

namespace DigitalWorldOnline.Management.Api.Data;

public class CreateEnterpriseIn
{
    public string Name { get; set; }
    public string CNPJ { get; set; }
    public string StateRegistration { get; set; }
    public string MunicipalRegistration { get; set; }

    public string Email { get; set; }
    public string Phone { get; set; }
    public string Mobile { get; set; }
    public string Website { get; set; }

    public string Street { get; set; }
    public string Number { get; set; }
    public string Complement { get; set; }
    public string Neighborhood { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public string PostalCode { get; set; }
    public string Country { get; set; }
    public bool IsActive { get; set; }

    public string ContactPerson { get; set; }
    public string ContactEmail { get; set; }
    public string ContactPhone { get; set; }

    public string LogoUrl { get; set; } 
    public string Notes { get; set; } 
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
