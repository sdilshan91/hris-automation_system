---
name: reference-payroll-payslip-pdf
description: US-PAY-004 payslip-PDF scaffold — QuestPDF + IFileStorage reuse, 3-seam split, where the deferred YTD/branding toggles live
metadata:
  type: reference
---

US-PAY-004 (payslip PDF generation) reusable seams + gotchas:

- **QuestPDF is ALREADY referenced** in `HRM.Infrastructure.csproj` (Version `2024.*`,
  added for US-ATT-007 attendance-summary PDF). Do NOT add a new package ref. Set
  `QuestPDF.Settings.License = LicenseType.Community` idempotently inside the render
  method (matches `OfferPdfRenderer`/AttendanceSummaryService pattern).
- **Blob storage = the existing `IFileStorage`** (`Application/Common/Interfaces`,
  impl `LocalFileStorage` in `Infrastructure/Services`, added US-CHR-001). It prefixes
  `{tenantId}/` itself, so callers pass only the WITHIN-tenant relative path. Reuse it;
  do not invent a storage abstraction. Recruitment resume download also uses it.
- **3-seam split mirrors the US-PAY-003 run-job pattern**: pure static renderer
  (`byte[]` from a denormalized model, no DB) → batch renderer (compute, bounded
  `SemaphoreSlim` concurrency, bulk-load to avoid N+1, one SaveChanges) → generation
  service (enqueue/status/list/download). Hangfire job restores tenant context from
  job args into a fresh DI scope (same as `ProcessPayrollRunJob`). The job scheduler
  interface is OPTIONAL in DI so tests/dev invoke the batch renderer directly.
- **Tenant YTD toggle + tenant payslip-template (logo/address/colour/footer) have NO
  config surface yet** — `PayslipBatchRenderer.TenantYtdEnabled()` returns false and
  branding falls back to subdomain/default disclaimer. The YTD sum logic
  (`BuildYtdAsync`) IS fully built; flipping it on is one line once a tenant
  payroll-settings entity exists. See [[reference-recruitment-module]] for the
  same optional-Hangfire-scheduler convention.

- **US-PAY-005 (employee self-service read) reuses ALL of the above** — no new entity/
  migration. The self-resolution pattern is `Employee.UserId == ICurrentUser.UserId`
  (nullable UserId ⇒ 403 `no_employee_linked`). PERMISSION GOTCHA: the story says
  `Payroll.Read.Self` but that's NOT in `PermissionCatalog`; the registered self-scope is
  `Payroll.View.Own` (granted to the Employee role), matching the `Module.View.Own`
  convention — use that, don't invent. AC-4 wants cross-employee = **403** (not 404):
  load slip tenant-scoped, then owner-check `slip.EmployeeId != mine ⇒ 403`. BR-1 =
  Finalized-run slips only. YTD/post-termination policy still deferred (same toggle surface).

Module domain notes live in `docs/vault/modules/payroll.md` (shared vault), not here.
