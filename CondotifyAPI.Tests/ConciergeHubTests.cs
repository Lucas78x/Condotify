using System.Security.Claims;
using CondotifyAPI.Hubs;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace CondotifyAPI.Tests;

public sealed class ConciergeHubTests
{
    private sealed class FakeLicenseAuthorizationService(bool allowed) : ILicenseAuthorizationService
    {
        public Task<LicenseAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, Guid licenseId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasPermissionAsync(ClaimsPrincipal principal, Guid licenseId, LicensePermissionEnum permission, CancellationToken cancellationToken = default) => Task.FromResult(allowed);
        public Task<HashSet<Guid>> GetAccessibleLicenseIdsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, LicensePermissionEnum>> GetLicensePermissionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HashSet<Guid>> GetLicenseIdsWithPermissionAsync(ClaimsPrincipal principal, LicensePermissionEnum permission, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public readonly List<(string ConnectionId, string GroupName)> Added = [];
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) { Added.Add((connectionId, groupName)); return Task.CompletedTask; }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ConciergeHub BuildHub(bool allowed, out FakeGroupManager groups)
    {
        groups = new FakeGroupManager();
        var hub = new ConciergeHub(new FakeLicenseAuthorizationService(allowed))
        {
            Groups = groups,
            Context = new HubCallerContextStub()
        };
        return hub;
    }

    private sealed class HubCallerContextStub : HubCallerContext
    {
        public override string ConnectionId { get; } = "conn-1";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User { get; } = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "TestAuth"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted { get; } = CancellationToken.None;
        public override void Abort() { }
    }

    [Fact]
    public async Task JoinLicenseGroup_WhenAuthorized_AddsToGroup()
    {
        var hub = BuildHub(allowed: true, out var groups);

        await hub.JoinLicenseGroup(Guid.NewGuid());

        Assert.Single(groups.Added);
    }

    [Fact]
    public async Task JoinLicenseGroup_WhenNotAuthorized_DoesNotAddToGroup()
    {
        var hub = BuildHub(allowed: false, out var groups);

        await hub.JoinLicenseGroup(Guid.NewGuid());

        Assert.Empty(groups.Added);
    }
}
