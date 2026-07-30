using System.Diagnostics.Metrics;

namespace HRM.Application.Common.Observability;

/// <summary>
/// US-PLT-004 (item 3): a small set of DOMAIN meters, mirroring the infrastructure-level
/// <c>HrmCacheMetrics</c>. The meter name is <c>HRM.Domain</c>, already collected by the OTel metrics
/// pipeline's <c>AddMeter("HRM.*")</c> wildcard (see <c>HRM.Api/Observability/ObservabilityExtensions.cs</c>),
/// so no registration change is needed — this is an EXTENSION of a proven pattern.
///
/// <para>Deliberately small (three well-placed instruments beat twenty scattered ones): a login-outcome
/// counter, a leave-request-submitted counter, and a payroll-run-duration histogram. Instrumented at the
/// natural service seams (the login/leave command handlers and the payroll run processor). Only OUTCOMES
/// and durations are recorded — never PII (no emails, employee ids, or amounts are tagged).</para>
///
/// <para>These record unconditionally: <see cref="Meter"/> instruments are inert (near-zero cost) when no
/// listener is attached, which is the shipped default (OTel is inert-by-default per ISSUE-345). There is no
/// need to branch on OTel state at the call site.</para>
/// </summary>
public static class HrmDomainMetrics
{
    public const string MeterName = "HRM.Domain";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Login attempts, tagged <c>outcome=success|failure</c> (no email/user id — no PII).</summary>
    private static readonly Counter<long> LoginOutcomes = Meter.CreateCounter<long>(
        name: "hrm.auth.login",
        unit: "{login}",
        description: "Login attempts, tagged outcome=success|failure.");

    /// <summary>Leave requests successfully submitted (created).</summary>
    private static readonly Counter<long> LeaveRequestsSubmitted = Meter.CreateCounter<long>(
        name: "hrm.leave.request.submitted",
        unit: "{request}",
        description: "Leave requests successfully submitted.");

    /// <summary>Wall-clock duration of a completed payroll run, in milliseconds.</summary>
    private static readonly Histogram<double> PayrollRunDuration = Meter.CreateHistogram<double>(
        name: "hrm.payroll.run.duration",
        unit: "ms",
        description: "Wall-clock duration of a completed payroll run, in milliseconds.");

    /// <summary>Records one login attempt outcome (success or failure).</summary>
    public static void RecordLogin(bool success)
        => LoginOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", success ? "success" : "failure"));

    /// <summary>Records one successfully-submitted leave request.</summary>
    public static void RecordLeaveRequestSubmitted()
        => LeaveRequestsSubmitted.Add(1);

    /// <summary>Records the wall-clock duration (ms) of a completed payroll run.</summary>
    public static void RecordPayrollRunDuration(double elapsedMilliseconds)
        => PayrollRunDuration.Record(elapsedMilliseconds);
}
