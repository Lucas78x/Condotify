using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Services.AccessControl;

namespace CondotifyAPI.Tests;

public class AccessOperationPolicyTests
{
    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(5, 32)]
    [InlineData(6, 60)]
    [InlineData(20, 60)]
    public void RetryDelay_UsesProgressiveDelayWithOneHourLimit(int attempt, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), AccessOperationPolicy.RetryDelay(attempt));
    }

    [Theory]
    [InlineData(AccessOperationItemStatusEnum.Queued, true, false)]
    [InlineData(AccessOperationItemStatusEnum.Running, false, false)]
    [InlineData(AccessOperationItemStatusEnum.WaitingDevice, true, true)]
    [InlineData(AccessOperationItemStatusEnum.Failed, true, true)]
    [InlineData(AccessOperationItemStatusEnum.Completed, false, false)]
    [InlineData(AccessOperationItemStatusEnum.Canceled, false, false)]
    [InlineData(AccessOperationItemStatusEnum.DeadLetter, false, true)]
    public void ItemActions_RespectOperationalState(AccessOperationItemStatusEnum status, bool canCancel, bool canRetry)
    {
        Assert.Equal(canCancel, AccessOperationPolicy.CanCancelItem(status));
        Assert.Equal(canRetry, AccessOperationPolicy.CanRetryItem(status));
    }

    [Fact]
    public void RefreshCounts_SeparatesProgressFromRetryableFailures()
    {
        var batch = new AccessBatchOperationDTO
        {
            Items = new List<AccessOperationItemDTO>
            {
                new() { Status = AccessOperationItemStatusEnum.Completed },
                new() { Status = AccessOperationItemStatusEnum.Canceled },
                new() { Status = AccessOperationItemStatusEnum.DeadLetter },
                new() { Status = AccessOperationItemStatusEnum.WaitingDevice },
                new() { Status = AccessOperationItemStatusEnum.Failed },
                new() { Status = AccessOperationItemStatusEnum.Running },
                new() { Status = AccessOperationItemStatusEnum.Queued }
            }
        };

        AccessOperationPolicy.RefreshCounts(batch);

        Assert.Equal(7, batch.TotalItems);
        Assert.Equal(3, batch.ProcessedItems);
        Assert.Equal(1, batch.SuccessfulItems);
        Assert.Equal(3, batch.FailedItems);
    }
}
