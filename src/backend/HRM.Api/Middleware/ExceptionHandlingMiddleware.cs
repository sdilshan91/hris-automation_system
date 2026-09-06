using System.Text.Json;
using FluentValidation;
using HRM.Application.Common.Exceptions;
using HRM.Application.DTOs;

namespace HRM.Api.Middleware;

/// <summary>
/// Global exception handling middleware that catches unhandled exceptions
/// and returns consistent ApiResponse error payloads.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation error");

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";

            var errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
            var response = ApiResponse.Fail(string.Join("; ", errors));

            await context.Response.WriteAsJsonAsync(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access");

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(ApiResponse.Fail("Unauthorized."));
        }
        catch (ForbiddenException ex)
        {
            // US-ADM-003: an impersonation read-only / destructive-op block surfaces here as a clean 403.
            _logger.LogWarning(ex, "Forbidden operation");

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(ApiResponse.Fail(ex.Message, "impersonation_forbidden"));
        }
        // ISSUE-376: a client that navigates away, times out or cancels an in-flight XHR aborts the
        // connection. EF Core, HttpClient and every `await ... (cancellationToken)` in the stack then throw
        // OperationCanceledException (TaskCanceledException derives from it), which fell into the catch-all
        // below and was logged as an ERROR with a 500 — for a request that failed because the USER LEFT.
        //
        // That is not a cosmetic log-noise problem. It puts non-faults into the same bucket the on-call
        // signal reads, so a genuine 500 spike is indistinguishable from a page with a slow table that
        // users abandon. This handler makes the distinction the middleware could not previously express.
        //
        // The exception FILTER is load-bearing. An unfiltered `catch (OperationCanceledException)` would
        // also swallow a server-side timeout — a linked CancellationTokenSource that fired on OUR deadline,
        // which is a real fault and must stay a 500. Only `context.RequestAborted.IsCancellationRequested`
        // identifies the client as the canceller. This mirrors ClamAvVirusScanner.cs:75, which uses the same
        // filtered form for the same reason, and deliberately NOT ApiCallCounterFlushService.cs:60, whose
        // unfiltered catch is safe only because a BackgroundService has a single unambiguous token.
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Information, not Warning or Error: nothing is wrong. Someone closed a tab.
            _logger.LogInformation(
                "Request aborted by the client. Path={Path}, Method={Method}",
                context.Request.Path, context.Request.Method);

            // 499 is Nginx-origin and non-standard on the wire, but it IS defined in ASP.NET Core
            // (StatusCodes.Status499ClientClosedRequest) and the framework's own diagnostics middleware
            // uses it for exactly this. Nobody receives it — the client is already gone — so its entire
            // value is that Serilog and the metrics pipeline record a distinct NON-5xx outcome.
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;

            // No body. Writing one would attempt a socket write to a closed connection, which throws
            // out of the exception handler itself.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");

            // A response already on the wire cannot be rewritten: setting StatusCode after HasStarted
            // throws InvalidOperationException, replacing this handler's error with a second one. Log and
            // let the connection die rather than fail inside the failure path.
            if (context.Response.HasStarted)
                return;

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var response = ApiResponse.Fail("An unexpected error occurred. Please try again later.");

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
