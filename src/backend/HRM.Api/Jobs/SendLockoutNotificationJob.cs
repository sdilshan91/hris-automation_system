using HRM.Application.Common.Interfaces;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire background job for sending account lockout notification emails (US-AUTH-010 FR-8, NFR-3).
/// Enqueued immediately when an account is locked; should execute within 60 seconds.
/// </summary>
public sealed class SendLockoutNotificationJob
{
    private readonly ILockoutNotificationService _notificationService;

    public SendLockoutNotificationJob(ILockoutNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    // ISSUE-063: tenantName added as the last DATA param (Hangfire serializes args by position — it must match
    // the enqueue call's argument order in AuthService).
    public async Task RunAsync(
        string userEmail, string? displayName, DateTime lockedUntilUtc, int lockoutDurationMinutes, string? tenantName)
    {
        try
        {
            await _notificationService.SendLockoutNotificationAsync(
                userEmail, displayName, lockedUntilUtc, lockoutDurationMinutes, tenantName);

            Log.Information(
                "Lockout notification job completed for {Email}", userEmail);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "Lockout notification job failed for {Email}", userEmail);
            throw; // Let Hangfire handle retry
        }
    }
}
