namespace HRM.Application.Common.Exceptions;

/// <summary>
/// Thrown when an authenticated caller is not permitted to perform an operation (HTTP 403). Distinct from
/// <see cref="UnauthorizedAccessException"/> (401 — not authenticated). Introduced for US-ADM-003 so the
/// <c>ImpersonationReadOnlyBehavior</c> can short-circuit a forbidden write in the MediatR pipeline and have it
/// surface as a clean 403 via <c>ExceptionHandlingMiddleware</c>, rather than a business <c>Result</c> failure
/// (the behavior runs before the handler, so it has no handler-shaped result to return).
/// </summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}
