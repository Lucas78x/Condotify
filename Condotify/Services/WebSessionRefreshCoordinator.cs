using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Condotify.Out;
using Microsoft.Extensions.Caching.Memory;

namespace Condotify.Services;

/// <summary>
/// Deduplica a rotacao de um refresh token entre abas e requisicoes concorrentes.
/// Sem essa coordenacao, duas abas podem reutilizar o mesmo token e a API revoga
/// corretamente toda a cadeia por suspeita de roubo.
/// </summary>
public sealed class WebSessionRefreshCoordinator
{
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Lazy<Task<LoginOut?>>> _inFlight = new();
    private readonly IMemoryCache _cache;

    public WebSessionRefreshCoordinator(IMemoryCache cache) => _cache = cache;

    public async Task<LoginOut?> RefreshAsync(
        string refreshToken,
        Func<CancellationToken, Task<LoginOut?>> refresh,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        ArgumentNullException.ThrowIfNull(refresh);

        var key = BuildCacheKey(refreshToken);
        if (_cache.TryGetValue<LoginOut>(key, out var cached))
            return cached;

        var operation = _inFlight.GetOrAdd(
            key,
            _ => new Lazy<Task<LoginOut?>>(
                () => ExecuteAsync(key, refresh),
                LazyThreadSafetyMode.ExecutionAndPublication));

        var task = operation.Value;
        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        finally
        {
            if (task.IsCompleted)
            {
                _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<LoginOut?>>>(key, operation));
            }
            else
            {
                _ = task.ContinueWith(
                    _ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<LoginOut?>>>(key, operation)),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    private async Task<LoginOut?> ExecuteAsync(
        string key,
        Func<CancellationToken, Task<LoginOut?>> refresh)
    {
        using var timeout = new CancellationTokenSource(RefreshTimeout);
        var result = await refresh(timeout.Token);
        if (IsSuccessful(result))
            _cache.Set(key, result!, ReplayWindow);

        return result;
    }

    private static bool IsSuccessful(LoginOut? result) =>
        result?.Result == "Success"
        && !string.IsNullOrWhiteSpace(result.AccessToken)
        && !string.IsNullOrWhiteSpace(result.RefreshToken);

    private static string BuildCacheKey(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return $"web-session-refresh:{Convert.ToHexString(hash)}";
    }
}
