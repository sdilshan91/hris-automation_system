using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Api.Middleware;

/// <summary>
/// US-ADM-003 (AC-3/NFR-2): session-based revocation + expiry enforcement for impersonation tokens. Slotted after
/// Authentication/Authorization (so <see cref="ICurrentUser"/> is populated) and before the controllers.
///
/// <para>For any request whose token carries <c>is_impersonation = true</c>, it loads the
/// <see cref="ImpersonationSession"/> by the token's <c>imp_session_id</c> and REJECTS the request with 401 when
/// the session is missing, not <see cref="ImpersonationSessionStatus.Active"/>, or past its
/// <see cref="ImpersonationSession.ExpiresAt"/>. An expired-but-still-Active row is lazily flipped to
/// <see cref="ImpersonationSessionStatus.Expired"/>. This is what makes "End Session" (AC-3) and the 60-minute
/// cap (NFR-2) effective WITHOUT an access-token denylist — killing the session row kills every future request.</para>
///
/// <para>On a permitted mutating request (non-GET/HEAD/OPTIONS) it best-effort increments
/// <see cref="ImpersonationSession.ActionsCount"/> so the End-Session audit can summarise activity (FR-4/AC-3).</para>
/// </summary>
public sealed class ImpersonationEnforcementMiddleware
{
    private static readonly HashSet<string> ReadMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS",
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ImpersonationEnforcementMiddleware> _logger;

    public ImpersonationEnforcementMiddleware(
        RequestDelegate next, ILogger<ImpersonationEnforcementMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var currentUser = context.RequestServices.GetRequiredService<ICurrentUser>();

        // Non-impersonated traffic is untouched.
        if (!currentUser.IsAuthenticated || !currentUser.IsImpersonating)
        {
            await _next(context);
            return;
        }

        var sessionId = currentUser.ImpersonationSessionId;
        if (sessionId is null)
        {
            await RejectAsync(context, "The impersonation token is missing a session reference.");
            return;
        }

        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var session = await db.ImpersonationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId.Value, context.RequestAborted);

        if (session is null)
        {
            await RejectAsync(context, "The impersonation session no longer exists.");
            return;
        }

        var now = DateTime.UtcNow;

        // Lazily expire an Active session whose hard expiry has passed (NFR-2).
        if (session.Status == ImpersonationSessionStatus.Active && session.ExpiresAt < now)
        {
            session.Status = ImpersonationSessionStatus.Expired;
            session.EndedAt ??= now;
            await db.SaveChangesAsync(context.RequestAborted);
        }

        if (session.Status != ImpersonationSessionStatus.Active)
        {
            _logger.LogWarning(
                "Rejected impersonated request: session {SessionId} is {Status}.", session.Id, session.Status);
            await RejectAsync(context, "The impersonation session has ended or expired.");
            return;
        }

        // Best-effort action counting for the End-Session summary (FR-4/AC-3). Mutating requests only.
        if (!ReadMethods.Contains(context.Request.Method))
        {
            try
            {
                session.ActionsCount += 1;
                await db.SaveChangesAsync(context.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to increment ActionsCount for impersonation session {SessionId}.", session.Id);
            }
        }

        await _next(context);
    }

    private static async Task RejectAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(message, "impersonation_session_invalid"));
    }
}
