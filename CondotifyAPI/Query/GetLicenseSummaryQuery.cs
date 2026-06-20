using CondotifyAPI.Data.Licenses;
using MediatR;

namespace CondotifyAPI.Query
{

    public record GetLicenseSummariesByUserQuery(string UserId) : IRequest<List<LicenseSummaryDto>>;
}
