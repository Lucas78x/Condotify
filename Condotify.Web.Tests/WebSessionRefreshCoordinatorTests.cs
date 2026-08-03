using Condotify.Out;
using Condotify.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Condotify.Web.Tests;

public sealed class WebSessionRefreshCoordinatorTests
{
    [Fact]
    public async Task RefreshAsync_DeduplicatesConcurrentRotationAndCachesThePair()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new WebSessionRefreshCoordinator(cache);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<LoginOut?> Rotate(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return SuccessfulPair();
        }

        var first = coordinator.RefreshAsync("same-refresh-token", Rotate);
        await entered.Task;
        var second = coordinator.RefreshAsync("same-refresh-token", Rotate);
        release.TrySetResult();

        var results = await Task.WhenAll(first, second);
        var cached = await coordinator.RefreshAsync("same-refresh-token", Rotate);

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Equal("new-refresh-token", result?.RefreshToken));
        Assert.Equal("new-refresh-token", cached?.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotCacheFailedRotation()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new WebSessionRefreshCoordinator(cache);
        var calls = 0;

        Task<LoginOut?> Reject(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<LoginOut?>(new LoginOut { Result = "InvalidToken" });
        }

        await coordinator.RefreshAsync("invalid-refresh-token", Reject);
        await coordinator.RefreshAsync("invalid-refresh-token", Reject);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RefreshAsync_KeepsSharedRotationAliveWhenOneCallerDisconnects()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new WebSessionRefreshCoordinator(cache);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var disconnectedCaller = new CancellationTokenSource();
        var calls = 0;

        async Task<LoginOut?> Rotate(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return SuccessfulPair();
        }

        var disconnected = coordinator.RefreshAsync(
            "shared-refresh-token",
            Rotate,
            disconnectedCaller.Token);
        await entered.Task;
        var active = coordinator.RefreshAsync("shared-refresh-token", Rotate);

        disconnectedCaller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disconnected);
        release.TrySetResult();

        var result = await active;
        Assert.Equal(1, calls);
        Assert.Equal("new-refresh-token", result?.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_RejectsBlankTokenBeforeCallingTheApi()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var coordinator = new WebSessionRefreshCoordinator(cache);
        var called = false;

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.RefreshAsync(
            " ",
            _ =>
            {
                called = true;
                return Task.FromResult<LoginOut?>(SuccessfulPair());
            }));

        Assert.False(called);
    }

    private static LoginOut SuccessfulPair() => new()
    {
        Result = "Success",
        AccessToken = "new-access-token",
        RefreshToken = "new-refresh-token",
        ExpiresIn = 3600
    };
}
