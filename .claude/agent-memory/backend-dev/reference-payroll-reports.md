---
name: reference-payroll-reports
description: US-PAY-009 payroll reports/analytics — reused leave-module export infra, report-type IDs, bank-fields gap, permission, deferrals
metadata:
  type: reference
---

US-PAY-009 (payroll reports + analytics) backend lives in `Features/Payroll` + `Infrastructure/Services`.
Full design + FE contract is in the **vault** `docs/vault/modules/payroll.md` (## Payroll reports + analytics).

Key reuse + decisions worth recalling before touching payroll/leave reports:
- **Export infra was REUSED, not re-built**: `IReportExportStorage`/`LocalReportExportStorage` (leave
  module US-LV-012), **ClosedXML** (.xlsx), **CsvHelper** (CSV), **QuestPDF** (PDF, from US-PAY-004). All
  three render paths are ONE pure fn `PayrollReportRenderer.Render(format, PayrollReportResult)`. Do NOT
  add a second export stack — extend the renderer.
- **Permission = `Payroll.Export`** (already in PermissionCatalog; do not invent a `Reports.*` gate).
- **Report-type path identifiers**: PayrollSummary, EmployeeRegister, DepartmentSummary,
  StatutoryDeduction, BankAdvice, Ctc, Variance (built) + YearEndTaxStatement (STUB).
  **Export-format wire values: csv / xlsx / pdf.** Chart types: MonthlyTrend,
  DepartmentCostDistribution, StatutoryBreakdown.
- **Employee entity has NO bank columns** (bank advice emits empty bank/branch/account; net+narration
  derived). `PayrollReportService.MaskAccount` (last-4, BR-2) is wired preview-masked/export-full for
  when the columns land. See [[reference-attendance-module]] / payroll vault note for the gap TODO.
- Reports are SYNCHRONOUS + tenant-scoped purely by the EF global filter; FINALIZED runs only (BR-1).
- Deferred: year-end tax statements (bulk PDF/ZIP), async-large (Hangfire), materialized table, NFR-4
  24h TTL purge (IReportExportStorage has none — shared leave-module gap).
</content>
