using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.License;
using CondotifyAPI.Domain.Models.Equipments;
using FluentValidation;
using MediatR;
using CondotifyAPI.Domain.Models;

namespace CondotifyAPI.Commands.Licenses
{
    public class CreateLicenseByEnterpriseCommand : IRequest<License>
    {
        public Guid EnterpriseId { get; set; }
        public string Name { get; set; }
        public string CNPJ { get; set; }
        public OrganizationTypeEnum Organization { get; set; }
        public BuildingTypeEnum Building { get; set; }
        public LicenseTypeEnum Type { get; set; }

        public Location Location { get; set; }
        public DateTime ExpireDate { get; set; }

        public CreateLicenseByEnterpriseCommand(
            Guid enterpriseId,
            string name,
            string cnpj,
            OrganizationTypeEnum organization,
            BuildingTypeEnum building,
            LicenseTypeEnum type,
            Location location,
            DateTime expireDate)
        {
            EnterpriseId = enterpriseId;
            Name = name;
            CNPJ = cnpj;
            Organization = organization;
            Building = building;
            Type = type;
            Location = location;
            ExpireDate = expireDate;
        }

        internal class Handler : IRequestHandler<CreateLicenseByEnterpriseCommand, License>
        {
            private readonly ICondotifyCommandsRepository _repository;

            public Handler(ICondotifyCommandsRepository repository)
            {
                _repository = repository;
            }

            public async Task<License> Handle(CreateLicenseByEnterpriseCommand request, CancellationToken cancellationToken)
            {
                var license = License.Create(
                    request.Name,
                    request.CNPJ,
                    request.Type,
                    request.Location,
                    request.ExpireDate,
                    DateTime.Now);
                
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
