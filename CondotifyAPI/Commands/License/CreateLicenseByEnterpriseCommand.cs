using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.License;
using CondotifyAPI.Domain.Models.Equipments;
using FluentValidation;
using MediatR;
using CondotifyAPI.Domain.Models;

namespace CondotifyAPI.Commands.Licenses
{
    public class CreateLicenseByEnterpriseCommand : IRequest<License?>
    {
        public Guid EnterpriseId { get; set; }
        public string Name { get; set; }
        public string CNPJ { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public string Code { get; set; }
        public OrganizationTypeEnum Organization { get; set; }
        public BuildingTypeEnum Building { get; set; }
        public LicenseTypeEnum Type { get; set; }

        public Location Location { get; set; }
        public DateTime ExpireDate { get; set; }

        public CreateLicenseByEnterpriseCommand(
            Guid enterpriseId,
            string name,
            string cnpj,
            string city,
            string country,
            string code,
            OrganizationTypeEnum organization,
            BuildingTypeEnum building,
            LicenseTypeEnum type,
            Location location,
            DateTime expireDate)
        {
            EnterpriseId = enterpriseId;
            Name = name;
            CNPJ = cnpj;
            City = city;
            Country = country;
            Code = code;
            Organization = organization;
            Building = building;
            Type = type;
            Location = location;
            // Date-only inputs from the Blazor calendar can arrive as Local (when
            // the JSON contains an offset) or Unspecified. PostgreSQL timestamptz
            // only accepts UTC DateTime values through Npgsql.
            ExpireDate = expireDate.Kind switch
            {
                DateTimeKind.Utc => expireDate,
                DateTimeKind.Local => expireDate.ToUniversalTime(),
                _ => DateTime.SpecifyKind(expireDate, DateTimeKind.Utc)
            };
        }

        internal class Handler : IRequestHandler<CreateLicenseByEnterpriseCommand, License?>
        {
            private readonly ICondotifyCommandsRepository _repository;

            public Handler(ICondotifyCommandsRepository repository)
            {
                _repository = repository;
            }

            public async Task<License?> Handle(CreateLicenseByEnterpriseCommand request, CancellationToken cancellationToken)
            {
                var license = License.Create(
                    request.Name,
                    request.CNPJ,
                    request.Type,
                    request.Location,
                    request.ExpireDate,
                    DateTime.UtcNow);

                license.City = request.City;
                license.Country = request.Country;
                license.Code = request.Code;
                license.Organization = request.Organization;
                license.Building = request.Building;
                
                return await _repository.AddLicenseAsync(request.EnterpriseId, license);
            }
        }
    }

    public class CreateLicenseByEnterpriseCommandValidator : AbstractValidator<CreateLicenseByEnterpriseCommand>
    {
        public CreateLicenseByEnterpriseCommandValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(200);

            RuleFor(x => x.CNPJ)
                .NotEmpty().WithMessage("O CNPJ é obrigatório.")
                .Length(14).WithMessage("O CNPJ deve ter 14 dígitos.")
                .Matches("^[0-9]+$").WithMessage("O CNPJ deve conter apenas números.");

            RuleFor(x => x.City)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.Country)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.Code)
                .NotEmpty()
                .MaximumLength(80);
                
            RuleFor(x => x.EnterpriseId)
                .NotEmpty();

            RuleFor(x => x.Type)
                .IsInEnum();

            RuleFor(x => x.ExpireDate)
                .GreaterThan(DateTime.UtcNow).WithMessage("ExpireDate deve ser no futuro.");

            RuleFor(x => x.Location)
                .NotNull().WithMessage("Location é obrigatório.");
        }
    }
}
