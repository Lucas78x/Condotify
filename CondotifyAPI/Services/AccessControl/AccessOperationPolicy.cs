using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Enums.AccessControl;

namespace CondotifyAPI.Services.AccessControl;

internal static class AccessOperationPolicy
{
    internal static TimeSpan RetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount, 1, 6);
        return TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, exponent)));
    }

    internal static bool CanCancelBatch(AccessBatchStatusEnum status) =>
        status is AccessBatchStatusEnum.Queued or AccessBatchStatusEnum.Running;

    internal static bool CanCancelItem(AccessOperationItemStatusEnum status) =>
        status is AccessOperationItemStatusEnum.Queued
            or AccessOperationItemStatusEnum.WaitingDevice
            or AccessOperationItemStatusEnum.Failed;

    internal static bool CanRetryItem(AccessOperationItemStatusEnum status) =>
        status is AccessOperationItemStatusEnum.WaitingDevice
            or AccessOperationItemStatusEnum.Failed
            or AccessOperationItemStatusEnum.DeadLetter;

    internal static bool IsPending(AccessOperationItemStatusEnum status) =>
        status is AccessOperationItemStatusEnum.Queued
            or AccessOperationItemStatusEnum.Running
            or AccessOperationItemStatusEnum.WaitingDevice
            or AccessOperationItemStatusEnum.Failed;

    internal static bool IsTerminal(AccessOperationItemStatusEnum status) =>
        status is AccessOperationItemStatusEnum.Completed
            or AccessOperationItemStatusEnum.Canceled
            or AccessOperationItemStatusEnum.DeadLetter;

    internal static void RefreshCounts(AccessBatchOperationDTO batch)
    {
        batch.TotalItems = batch.Items.Count;
        batch.SuccessfulItems = batch.Items.Count(x => x.Status == AccessOperationItemStatusEnum.Completed);
        batch.FailedItems = batch.Items.Count(x => x.Status is
            AccessOperationItemStatusEnum.WaitingDevice or
            AccessOperationItemStatusEnum.Failed or
            AccessOperationItemStatusEnum.DeadLetter);
        batch.ProcessedItems = batch.Items.Count(x => IsTerminal(x.Status));
    }
}
