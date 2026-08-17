using CondotifyAPI.Data.Licenses;
using CondotifyAPI.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace CondotifyAPI.Query
{
    public class GetLicenseSummariesByUserQueryHandler
        : IRequestHandler<GetLicenseSummariesByUserQuery, List<LicenseSummaryDto>>
    {
        private readonly ICondotifyQueriesRepository _repository;

        public GetLicenseSummariesByUserQueryHandler(ICondotifyQueriesRepository repository)
        {
            _repository = repository;
        }

        public async Task<List<LicenseSummaryDto>> Handle(GetLicenseSummariesByUserQuery request, CancellationToken cancellationToken)
        {
            // Valida a query
            var validator = new GetLicenseSummariesByUserQueryValidator();
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
                throw new ValidationException(validationResult.Errors);

            var user = await _repository.GetUserByIdAsync(Guid.Parse(request.UserId));
            if (user == null) return new List<LicenseSummaryDto>();

            var enterprise = await _repository.GetEnterpriseByIdAsync(user.EnterpriseId);
            if (enterprise == null) return new List<LicenseSummaryDto>();

            var licenses = await _repository.GetLicensesByEnterpriseAsync(enterprise.Id);

            // Retorna dados resumidos
            return licenses.Select(ToSummary).ToList();
        }

        internal static LicenseSummaryDto ToSummary(CondotifyAPI.Domain.Models.License.License license) =>
            new()
            {
                Id = license.Id,
                Nome = license.Name,
                Codigo = license.Code,
                UrlKey = license.UrlKey,
                Moradores = license.Blocks?.Sum(b =>
                    b.Units.Sum(u =>
                        u.Residents.Count(r => r.AccessType == ResidentAccessTypeEnum.Responsible
                                             || r.AccessType == ResidentAccessTypeEnum.NonResponsible)
                    )
                    ) ?? 0,
                Cidade = license.City,
                Estado = license.Country,
                EnabledModules = (long)license.EnabledModules
            };
    }

    // Validator
    public class GetLicenseSummariesByUserQueryValidator
        : AbstractValidator<GetLicenseSummariesByUserQuery>
    {
        public GetLicenseSummariesByUserQueryValidator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId não pode ser vazio")
                .Must(BeAValidGuid).WithMessage("UserId deve ser um GUID válido");
        }

        private bool BeAValidGuid(string userId) => Guid.TryParse(userId, out _);
    }
}
