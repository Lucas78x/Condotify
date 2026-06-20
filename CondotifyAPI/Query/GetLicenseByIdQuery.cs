using CondotifyAPI.ViewModels;
using MediatR;

namespace CondotifyAPI.Query
{
    public record GetLicenseByIdQuery(Guid LicenseId) : IRequest<LicenseSummaryViewModel?>;

}
