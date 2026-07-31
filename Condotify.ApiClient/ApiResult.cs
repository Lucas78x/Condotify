using System.Net;

namespace Condotify.Services;

public sealed record ApiResult<T>(bool Success, T? Value, string? Error, HttpStatusCode? StatusCode = null)
{
    public static ApiResult<T> Ok(T value, HttpStatusCode? statusCode = null) => new(true, value, null, statusCode);
    public static ApiResult<T> Fail(string error, HttpStatusCode? statusCode = null) => new(false, default, error, statusCode);
}
