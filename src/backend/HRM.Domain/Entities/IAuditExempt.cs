namespace HRM.Domain.Entities;

/// <summary>
/// BUG-082 / US-NTF-004: OPT-OUT marker that EXCLUDES a <see cref="BaseEntity"/> from the automatic
/// generic INSERT/UPDATE/DELETE capture performed by <c>AuditCaptureInterceptor</c>.
///
/// <para>Since BUG-082, auditing is opt-OUT: <c>AuditCaptureInterceptor</c> captures EVERY tenant
/// <see cref="BaseEntity"/> change into the shared <see cref="AuditLog"/> table (with property-level
/// before/after diffs) UNLESS the entity is marked with this interface. This closes the US-NTF-004
/// AC-1/FR-1 gap where most business entities had no audit trail at all.</para>
///
/// <para><b>An entity is exempt for exactly one of two reasons:</b></para>
/// <list type="number">
/// <item><b>Explicit-writer (avoid double-audit).</b> The entity's own service already writes a
/// domain-specific audit row (e.g. Payroll via <c>IPayrollAuditLogger</c>, Employee field changes via
/// <c>EmployeeFieldAuditLog</c>, recruitment/admin actions via explicit <see cref="AuditLog"/> writes).
/// Letting the interceptor also capture it would produce a duplicate, less-meaningful row per operation.</item>
/// <item><b>High-volume / infrastructure (NFR-1).</b> The entity churns at a rate where a generic audit
/// row per write would bloat the audit table and add latency for no compliance value (e.g. per-recipient
/// notifications, ledger/summary rows, per-line payslip detail).</item>
/// </list>
///
/// <para>Marker is empty by design: it carries no behavior, only the "do NOT auto-audit me" intent.
/// Before adding it to an entity, confirm the entity truly has its own writer (reason 1) or is genuinely
/// high-volume (reason 2) — otherwise it silently loses its audit trail.</para>
/// </summary>
public interface IAuditExempt
{
}
