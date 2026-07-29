using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.Enterprises;
using FluentValidation;
using MediatR;

namespace CondotifyAPI.Commands.Enterprises;

public class CreateEnterpriseCommand : IRequest<EnterpriseCreateResult>
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

    public CreateEnterpriseCommand(
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
        string notes)
    {
        Name = name;
        CNPJ = cnpj;
        StateRegistration = stateRegistration;
        MunicipalRegistration = municipalRegistration;
        Email = email;
        Phone = phone;
        Mobile = mobile;
        Website = website;
        Street = street;
        Number = number;
        Complement = complement;
        Neighborhood = neighborhood;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
        IsActive = isActive;
        ContactPerson = contactPerson;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        LogoUrl = logoUrl;
        Notes = notes;
    }

    internal class Handler : IRequestHandler<CreateEnterpriseCommand, EnterpriseCreateResult>
    {
        private readonly ICondotifyCommandsRepository _repository;

        public Handler(ICondotifyCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<EnterpriseCreateResult> Handle(CreateEnterpriseCommand request, CancellationToken cancellationToken)
        {
            var enterprise = Enterprise.Create(
                request.Name,
                request.CNPJ,
                request.StateRegistration,
                request.MunicipalRegistration,
                request.Email,
                request.Phone,
                request.Mobile,
                request.Website,
                request.Street,
                request.Number,
                request.Complement,
                request.Neighborhood,
                request.City,
                request.State,
                request.PostalCode,
                request.Country,
                request.IsActive,
                request.ContactPerson,
                request.ContactEmail,
                request.ContactPhone,
                request.LogoUrl,
                request.Notes,
                DateTime.UtcNow,
                DateTime.UtcNow);

            return await _repository.AddEnterpriseAsync(enterprise);
        }
    }
}

public class CreateEnterpriseCommandValidator : AbstractValidator<CreateEnterpriseCommand>
{
    public CreateEnterpriseCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.CNPJ)
            .NotEmpty()
            .Length(14);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Phone)
            .MaximumLength(20);

        RuleFor(x => x.Mobile)
            .MaximumLength(20);

        RuleFor(x => x.Website)
            .MaximumLength(100);

        RuleFor(x => x.PostalCode)
            .MaximumLength(10);
    }
}
