using Condotify.Mobile.Core;

namespace Condotify.Mobile.Tests;

public sealed class MobileOfflineOwnershipTests
{
    [Fact]
    public void BelongsToCurrentSession_RequiresSameNonEmptyIdentity()
    {
        var userId = Guid.NewGuid();

        Assert.True(MobileOfflineOwnership.BelongsToCurrentSession(userId, userId));
        Assert.False(MobileOfflineOwnership.BelongsToCurrentSession(userId, Guid.NewGuid()));
        Assert.False(MobileOfflineOwnership.BelongsToCurrentSession(userId, null));
        Assert.False(MobileOfflineOwnership.BelongsToCurrentSession(Guid.Empty, Guid.Empty));
    }
}
