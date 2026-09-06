namespace HRM.Application.Common.Models;

/// <summary>
/// Result pattern for returning success/failure without exceptions for business logic.
/// </summary>
public sealed class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public int? StatusCode { get; }

    /// <summary>
    /// Optional machine-readable error code surfaced in the response envelope (e.g. "already_clocked_in")
    /// so clients can branch on a stable identifier rather than the localized message or HTTP status.
    /// </summary>
    public string? ErrorCode { get; }

    private Result(bool isSuccess, string? error, int? statusCode, string? errorCode)
    {
        IsSuccess = isSuccess;
        Error = error;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true, null, null, null);
    public static Result Failure(string error, int statusCode = 400, string? errorCode = null)
        => new(false, error, statusCode, errorCode);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(string error, int statusCode = 400, string? errorCode = null)
        => Result<T>.Failure(error, statusCode, errorCode);
}

/// <summary>
/// Generic result with a value on success.
/// </summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public string? Error { get; }
    public int? StatusCode { get; }

    /// <summary>
    /// Optional machine-readable error code surfaced in the response envelope (e.g. "already_clocked_in")
    /// so clients can branch on a stable identifier rather than the localized message or HTTP status.
    /// </summary>
    public string? ErrorCode { get; }

    private Result(bool isSuccess, T? value, string? error, int? statusCode, string? errorCode)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public static Result<T> Success(T value) => new(true, value, null, null, null);
    public static Result<T> Failure(string error, int statusCode = 400, string? errorCode = null)
        => new(false, default, error, statusCode, errorCode);

    /// <summary>
    /// A failure that still carries structured data for the client to render.
    /// </summary>
    /// <remarks>
    /// ISSUE-367: a 409 whose only machine-readable content is an error CODE forces the client either to
    /// parse numbers out of a human message or to invent them. The salary-component delete dialog did the
    /// latter — it read an <c>affectedEmployeeCount</c> the API never sent and rendered the fallback,
    /// telling users a component was in use by "0 active employees" while refusing to delete it.
    /// </remarks>
    public static Result<T> Failure(T value, string error, int statusCode = 400, string? errorCode = null)
        => new(false, value, error, statusCode, errorCode);
}
