using CondotifyAPI.Domain.Models.Audit;

namespace CondotifyAPI.Tests;

public sealed class UtcPersistenceTests
{
    [Fact]
    public void DeviceAudit_ShouldAlwaysUseUtcTimestamps()
    {
        var audit = DeviceAudit.Create("Name", Guid.NewGuid(), "Tester");

        Assert.Equal(DateTimeKind.Utc, audit.Timestamp.Kind);

        audit.Update(ActionTypeEnum.Update, "IPAddress");

        Assert.Equal(DateTimeKind.Utc, audit.Timestamp.Kind);
    }
}
