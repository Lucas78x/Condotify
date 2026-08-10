using CondotifyAPI.Hubs;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace CondotifyAPI.Tests;

public sealed class ConciergeHubPublishTests
{
    [Fact]
    public async Task PublishToLicenseGroup_SendsToCorrectGroupAndMethod()
    {
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Group(ConciergeHub.GroupName(TestLicenseId))).Returns(clientProxy.Object);
        var hubContext = new Mock<IHubContext<ConciergeHub>>();
        hubContext.Setup(x => x.Clients).Returns(clients.Object);

        await hubContext.Object.Clients.Group(ConciergeHub.GroupName(TestLicenseId))
            .SendAsync("VisitStatusChanged", new { Id = Guid.NewGuid() });

        clients.Verify(x => x.Group(ConciergeHub.GroupName(TestLicenseId)), Times.Once);
        clientProxy.Verify(x => x.SendCoreAsync("VisitStatusChanged", It.IsAny<object[]>(), default), Times.Once);
    }

    private static readonly Guid TestLicenseId = Guid.NewGuid();
}
