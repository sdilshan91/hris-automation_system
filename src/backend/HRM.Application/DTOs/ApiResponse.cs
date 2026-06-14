namespace HRM.Application.DTOs;

/// <summary>
/// Consistent API response wrapper used by all endpoints.
/// </summary>
public sealed record ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }

    /// <summary>
    /// Optional machine-readable error code (e.g. "already_clocked_in") set on failures so clients
    /// can branch on a stable identifier with an HTTP-status fallback. Null on success.
    /// </summary>
    public string? Code { get; init; }

    public IReadOnlyList<string>? Errors { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };

    public static ApiResponse<T> Fail(string error) => new()
    {
        Success = false,
        Message = error,
        Errors = [error]
    };

    public static ApiResponse<T> Fail(string error, string? code) => new()
    {
        Success = false,
        Message = error,
        Code = code,
        Errors = [error]
    };

    public static ApiResponse<T> Fail(IReadOnlyList<string> errors) => new()
    {
        Success = false,
        Message = errors.FirstOrDefault(),
        Errors = errors
    };
}

/// <summary>
/// Non-generic API response for operations that return no data.
/// </summary>
public sealed record ApiResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }

    /// <summary>
    /// Optional machine-readable error code (e.g. "already_clocked_in") set on failures so clients
    /// can branch on a stable identifier with an HTTP-status fallback. Null on success.
    /// </summary>
    public string? Code { get; init; }

    public IReadOnlyList<string>? Errors { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse Ok(string? message = null) => new()
    {
        Success = true,
        Message = message
    };

    public static ApiResponse Fail(string error) => new()
    {
        Success = false,
        Message = error,
        Errors = [error]
    };

    public static ApiResponse Fail(string error, string? code) => new()
    {
        Success = false,
        Message = error,
        Code = code,
        Errors = [error]
    };
}
