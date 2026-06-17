using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Exit-interview recording + analytics service (US-ONB-006). All operations are tenant-scoped via
/// ITenantContext + the EF global query filter (FR-6/NFR-2/AC-5); the tenant_id is stamped from the session,
/// never user input. Recording completes the offboarding exit-interview task (AC-2) and, for self-service,
/// writes an HR-notification outbox row (FR-8). Analytics returns anonymized aggregates only unless the caller
/// is detail-permitted (FR-5). Audit columns are stamped by AuditInterceptor (FR-7).
/// </summary>
public interface IExitInterviewService
{
    /// <summary>FR-1/AC-1: the tenant's active questionnaire template (seeding a DEFAULT set when none exists).</summary>
    Task<Result<ExitInterviewTemplateDto>> GetTemplateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-3: the exit interview for an offboarding instance (the active version), or 404.
    /// <paramref name="includeFreeText"/> includes PII free-text answers + comments only for detail-permitted
    /// callers (FR-5/NFR-6).
    /// </summary>
    Task<Result<ExitInterviewDto>> GetByOffboardingAsync(
        Guid offboardingInstanceId, bool includeFreeText, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-2/FR-3: record (or, when an interview already exists, version, BR-2) an exit interview. Persists the
    /// responses, links to the offboarding, completes the exit-interview offboarding task, and (self-service)
    /// writes an HR-notification outbox row (FR-8). <paramref name="isSelfService"/> drives BR-3 (before LWD) +
    /// the notification, and scopes a self-service caller to their OWN offboarding (the caller's employee record
    /// is resolved from the session). BR-1: a second HR-conducted record for an interview that already exists is
    /// rejected unless <paramref name="allowEdit"/> (detail-permitted HR re-version).
    /// </summary>
    Task<Result<ExitInterviewDto>> RecordAsync(
        RecordExitInterviewInput input,
        bool isSelfService,
        bool allowEdit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-4: tenant-scoped aggregated analytics over a date range. Returns AGGREGATES ONLY (FR-5 anonymized);
    /// individual free-text answers are never included. <paramref name="hasDetailPermission"/> is recorded for
    /// the NFR-6 audit flag and reserved for any future de-anonymized drill-down.
    /// </summary>
    Task<Result<ExitInterviewAnalyticsDto>> GetAnalyticsAsync(
        DateTime? fromDate,
        DateTime? toDate,
        bool hasDetailPermission,
        CancellationToken cancellationToken = default);
}
