using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.ViewModels;
using MediatR;

namespace CondotifyAPI.Query
{
    public class GetLicenseByIdHandler : IRequestHandler<GetLicenseByIdQuery, LicenseSummaryViewModel?>
    {
        private readonly ICondotifyQueriesRepository _repository;

        public GetLicenseByIdHandler(ICondotifyQueriesRepository repository)
        {
            _repository = repository;
        }

        public async Task<LicenseSummaryViewModel?> Handle(GetLicenseByIdQuery request, CancellationToken cancellationToken)
        {

            var license = await _repository.GetLicenseByIdAsync(request.LicenseId);
            if (license == null)
                return null; 

            return LicenseSummaryViewModel.FromDomain(license);
        }
    }
}
