namespace CondotifyAPI.Domain.Models.Enterprises
{
    public class Enterprise
    {
        public Enterprise() { }

        public Guid Id { get; set; }

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

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        private Enterprise(
            string name,
            string cnpj,
            string stateRegistration,
            string municipalRegistration,
            string email,
            string phone,
            string mobile,
            string website,
            string street,
            string number,
            string complement,
            string neighborhood,
            string city,
            string state,
            string postalCode,
            string country,
            bool isActive,
            string contactPerson,
            string contactEmail,
            string contactPhone,
            string logoUrl,
            string notes,
            DateTime createdAt,
            DateTime updatedAt)
        {
            Id = Guid.NewGuid();
            SetName(name);
            SetCNPJ(cnpj);
            SetStateRegistration(stateRegistration);
            SetMunicipalRegistration(municipalRegistration);
            SetEmail(email);
            SetPhone(phone);
            SetMobile(mobile);
            SetWebsite(website);
            SetStreet(street);
            SetNumber(number);
            SetComplement(complement);
            SetNeighborhood(neighborhood);
            SetCity(city);
            SetState(state);
            SetPostalCode(postalCode);
            SetCountry(country);
            SetIsActive(isActive);
            SetContactPerson(contactPerson);
            SetContactEmail(contactEmail);
            SetContactPhone(contactPhone);
            SetLogoUrl(logoUrl);
            SetNotes(notes);
            SetCreatedAt(createdAt);
            SetUpdatedAt(updatedAt);
        }

        public static Enterprise Create(
            string name,
            string cnpj,
            string stateRegistration,
            string municipalRegistration,
            string email,
            string phone,
            string mobile,
            string website,
            string street,
            string number,
            string complement,
            string neighborhood,
            string city,
            string state,
            string postalCode,
            string country,
            bool isActive,
            string contactPerson,
            string contactEmail,
            string contactPhone,
            string logoUrl,
            string notes,
            DateTime createdAt,
            DateTime updatedAt)
        {
            return new Enterprise(
                name,
                cnpj,
                stateRegistration,
                municipalRegistration,
                email,
                phone,
                mobile,
                website,
                street,
                number,
                complement,
                neighborhood,
                city,
                state,
                postalCode,
                country,
                isActive,
                contactPerson,
                contactEmail,
                contactPhone,
                logoUrl,
                notes,
                createdAt,
                updatedAt
            );
        }

        public bool Update(
            string name,
            string cnpj,
            string stateRegistration,
            string municipalRegistration,
            string email,
            string phone,
            string mobile,
            string website,
            string street,
            string number,
            string complement,
            string neighborhood,
            string city,
            string state,
            string postalCode,
            string country,
            bool isActive,
            string contactPerson,
            string contactEmail,
            string contactPhone,
            string logoUrl,
            string notes,
            DateTime updatedAt)
        {
            SetName(name);
            SetCNPJ(cnpj);
            SetStateRegistration(stateRegistration);
            SetMunicipalRegistration(municipalRegistration);
            SetEmail(email);
            SetPhone(phone);
            SetMobile(mobile);
            SetWebsite(website);
            SetStreet(street);
            SetNumber(number);
            SetComplement(complement);
            SetNeighborhood(neighborhood);
            SetCity(city);
            SetState(state);
            SetPostalCode(postalCode);
            SetCountry(country);
            SetIsActive(isActive);
            SetContactPerson(contactPerson);
            SetContactEmail(contactEmail);
            SetContactPhone(contactPhone);
            SetLogoUrl(logoUrl);
            SetNotes(notes);
            SetUpdatedAt(updatedAt);

            return true;
        }

        #region Setters
        public void SetCreatedAt(DateTime createdAt) => CreatedAt = createdAt;
        public void SetUpdatedAt(DateTime updatedAt) => UpdatedAt = updatedAt;

        public void SetName(string name) => Name = name;
        public void SetCNPJ(string cnpj) => CNPJ = cnpj;
        public void SetStateRegistration(string stateRegistration) => StateRegistration = stateRegistration;
        public void SetMunicipalRegistration(string municipalRegistration) => MunicipalRegistration = municipalRegistration;
        public void SetEmail(string email) => Email = email;
        public void SetPhone(string phone) => Phone = phone;
        public void SetMobile(string mobile) => Mobile = mobile;
        public void SetWebsite(string website) => Website = website;
        public void SetStreet(string street) => Street = street;
        public void SetNumber(string number) => Number = number;
        public void SetComplement(string complement) => Complement = complement;
        public void SetNeighborhood(string neighborhood) => Neighborhood = neighborhood;
        public void SetCity(string city) => City = city;
        public void SetState(string state) => State = state;
        public void SetPostalCode(string postalCode) => PostalCode = postalCode;
        public void SetCountry(string country) => Country = country;
        public void SetIsActive(bool isActive) => IsActive = isActive;
        public void SetContactPerson(string contactPerson) => ContactPerson = contactPerson;
        public void SetContactEmail(string contactEmail) => ContactEmail = contactEmail;
        public void SetContactPhone(string contactPhone) => ContactPhone = contactPhone;
        public void SetLogoUrl(string logoUrl) => LogoUrl = logoUrl;
        public void SetNotes(string notes) => Notes = notes;
        #endregion
    }
}
