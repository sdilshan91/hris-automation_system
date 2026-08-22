---
name: reference-payroll-audit-bug080
description: BUG-080 US-PAY-012 — 7 payroll audit emitters wired via IPayrollAuditLogger.Log (staged, atomic) + audit_log RLS gap gotcha
metadata:
  type: reference
---

BUG-080 (HIGH, US-PAY-012 BR-1 "every payroll write emits an audit entry, no exceptions"): 7 missing
emitters were wired into the existing seams using `IPayrollAuditLogger.Log(...)` (STAGES an AuditLog row on
the current AppDbContext — committed atomically by the service's own `SaveChangesAsync`; NOT `LogAndSaveAsync`
which double-saves). Action/ResourceType constants all live in `HRM.Domain/Payroll/PayrollAuditAction.cs` —
never invent strings.

The 7 emitters + constructors that gained `IPayrollAuditLogger`:
- **SalaryStructureService** (ctor +audit): CreateAsync→`SalaryStructureCreated`; UpdateAsync→`SalaryStructureUpdated` (before-snapshot captured pre-mutation); CloneAsync→`SalaryStructureCreated` (a clone is a NEW structure).
- **SalaryAssignmentService** (ctor +audit): emitted ONCE in `AssignInternalAsync` (both AssignAsync + per-item BulkAssignAsync flow through it). `currentRows.Count > 0` (a prior ACTIVE assignment exists) ⇒ `EmployeeSalaryRevised`, else `EmployeeSalaryAssigned`. resourceId = employeeId.
- **PayrollRunProcessor** (ctor +audit): before the final SaveChanges where status→ReviewPending / CompletedAt set → `PayrollRunCompleted`, `systemActor: true` (Hangfire job, no HTTP user).
- **PayslipBatchRenderer** (ctor +audit): in `PersistRenderResultsAsync` per `outcome.Ok` slip (PdfStatus=Generated) → `PayslipPdfGenerated`/ResourceType.PayrollSlip, systemActor:true, resourceId=slip.Id. One row per slip (BR-1 no exceptions).
- **PayslipDistributionRunner** (ctor +audit): in `PersistSendOutcomeAsync` when `outcome.Status == Sent` → `PayslipEmailSent`/ResourceType.PayrollSlip (no PayslipEmailLog resource type), systemActor:true, resourceId=payrollSlipId.

Payload rule: after = small anon object of structural fields only (id/employeeId/structureId/effectiveDate/payYear/payMonth/status) — NEVER gross/CTC amounts, bank/national-id (PII).

Not done: `PayrollRun.Cancelled` — no run-cancel operation exists to hook (per spec, left).

**⚠ DURABLE GOTCHA — audit_log has NO RLS policy (Rls IMPLEMENTATION-DESIGN R1 gap).** `audit_log` carries
tenant_id but has no query filter → no dormant tenant_isolation policy → the PRODUCTION reconciler never
ENABLEs RLS on it, so these new job-path audit writes are safe in prod. BUT `PayslipJobRlsPostgresTests`
SIMULATES RLS by force-enabling RLS on EVERY tenant_id table indiscriminately — with FORCE RLS + no policy =
default-deny, so the payslip jobs' new audit_log inserts would fail-closed (42501). Fix: excluded
`audit_log` from that test's ENABLE/FORCE loop (`AND c.table_name <> 'audit_log'`) to match production
reconciler behavior. Any future job that writes an un-policied tenant_id table under that test hits the same.

Tests: extended the existing InMemory integration suites (each asserts an audit_logs row with the expected
Action) — SalaryStructureIntegrationTests (Created/Updated/Clone), SalaryAssignmentIntegrationTests
(Assigned/Revised/bulk-per-item), PayrollRunIntegrationTests (Completed, system actor), PayslipGeneration
IntegrationTests (PdfGenerated), PayslipDistributionIntegrationTests (EmailSent). Unit-test service ctors
(Salary*ServiceTests, PayslipDistributionTests, PayslipJobPhaseTests) needed the extra IPayrollAuditLogger
arg (Substitute/NoOp). Integration DIs that lacked it needed `AddScoped<IPayrollAuditLogger,PayrollAuditLogger>()`
+ an ICurrentUser registration (PayrollAuditLogger requires ICurrentUser even for systemActor writes).
