namespace HRM.Domain.Entities;

/// <summary>
/// A saved scheduled attendance-report configuration (US-ATT-010 §7, FR-8). HR configures a pre-built
/// report to be auto-generated on a cadence (DAILY/WEEKLY/MONTHLY) and delivered to a set of recipients.
///
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter (NFR-4 RLS is NOT
/// used — same deferral as the rest of the Attendance module). Maps to "scheduled_report_config".
///
/// EMAIL DELIVERY DEFERRED (US-NTF): there is no notification/email infrastructure in this codebase.
/// The Hangfire recurring job (<c>ScheduledReportJob</c>) finds due configs and GENERATES the report,
/// but the actual send is logged/no-op (TODO US-NTF). The entity + CRUD + job are the deliverable here.
/// </summary>
public sealed class ScheduledReportConfig : BaseEntity
{
    /// <summary>The pre-built report type, e.g. "DEPARTMENT_COMPARISON" / "CUSTOM" (§7).</summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>Delivery cadence: "DAILY", "WEEKLY", or "MONTHLY" (§7).</summary>
    public string Frequency { get; set; } = string.Empty;

    /// <summary>Saved filter configuration serialized as JSON (jsonb, §7). Empty object when no filters.</summary>
    public string FiltersJson { get; set; } = "{}";

    /// <summary>Recipient user IDs (uuid[], §7). Email delivery is deferred (US-NTF).</summary>
    public List<Guid> Recipients { get; set; } = new();

    /// <summary>Time of day to send, wall-clock "HH:mm" (treated as UTC — no tenant-timezone infra, §7/BR-6).</summary>
    public TimeOnly DeliveryTime { get; set; }

    /// <summary>Export format: "CSV", "XLSX", or "PDF" (§7).</summary>
    public string Format { get; set; } = "CSV";

    /// <summary>Whether the schedule is active (§7).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the job last successfully generated this report; null until first run.</summary>
    public DateTime? LastRunAt { get; set; }
}
