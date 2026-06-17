namespace HRM.Domain.Entities;

/// <summary>
/// US-NTF-004: OPT-IN marker for entities whose generic INSERT/UPDATE/DELETE changes should be captured
/// automatically into the shared <see cref="AuditLog"/> table by <c>AuditCaptureInterceptor</c> with
/// before/after property-level diffs (FR-1/BR-2/BR-3).
///
/// <para>This is deliberately OPT-IN (not "every <see cref="BaseEntity"/>") to avoid noise and — critically —
/// to avoid DOUBLE audit rows on entities whose own service ALREADY writes an explicit audit trail
/// (e.g. payroll via <c>IPayrollAuditLogger</c>, employee field changes via <c>EmployeeFieldAuditLog</c>,
/// admin/recruitment actions via explicit <see cref="AuditLog"/> writes). Mark an entity here ONLY after
/// confirming its service does not already log create/update/delete itself.</para>
///
/// <para>Marker is empty by design: it carries no behavior, only the "capture me automatically" intent.</para>
/// </summary>
public interface IAuditableEntity
{
}
