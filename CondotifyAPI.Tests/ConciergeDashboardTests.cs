using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.Enums.Invitation;

namespace CondotifyAPI.Tests;

public sealed class ConciergeDashboardTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 14, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(AccessVisitStatusEnum.Scheduled, -1, 1, true)]
    [InlineData(AccessVisitStatusEnum.PendingApproval, -1, 1, true)]
    [InlineData(AccessVisitStatusEnum.PendingEnrollment, -1, 1, true)]
    [InlineData(AccessVisitStatusEnum.CheckedIn, -72, -48, true)]
    [InlineData(AccessVisitStatusEnum.Canceled, -1, 1, false)]
    [InlineData(AccessVisitStatusEnum.Denied, -1, 1, false)]
    [InlineData(AccessVisitStatusEnum.CheckedOut, -1, 1, false)]
    [InlineData(AccessVisitStatusEnum.Expired, -1, 1, false)]
    [InlineData(AccessVisitStatusEnum.Scheduled, -2, -1, false)]
    [InlineData(AccessVisitStatusEnum.Scheduled, 8 * 24, 9 * 24, false)]
    public void OperationalVisitFilter_KeepsOnlyActionableAgenda(
        AccessVisitStatusEnum status,
        int fromHours,
        int toHours,
        bool expected)
    {
        var visit = new AccessVisitDTO
        {
            Status = status,
            ValidFrom = Now.AddHours(fromHours),
            ValidTo = Now.AddHours(toHours)
        };

        Assert.Equal(expected, ConciergeController.OperationalVisitFilter(Now).Compile()(visit));
    }
}
