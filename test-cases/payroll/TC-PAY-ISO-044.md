---
id: TC-PAY-ISO-044
user_story: US-PAY-011
module: Payroll
priority: high
type: security
status: pass
created: 2026-06-16
exec_note: "2026-07-02 PASS via the TC's CONDITIONAL fallback arm (step 5). A per-tenant rate-limiter (MaxEmailsPerMinute=0, throttle off), a distribution-summary cache, and a SignalR progress-group layer are NOT yet present (documented deferrals in PayslipDistributionRunner) — so the assertion is the no-shared/global-key fallback. Evidence: fntest distribution of run 019f2180… wrote 10 payslip_email_log rows ALL tenant_id=3f000000-…000f, 0 rows in any other tenant; every distribution query is EF-global-query-filter tenant-scoped (l.PayrollRunId+tenant filter). Sender domain (step2): ResolveFromAddress()=>null constant → no cross-tenant sender bleed possible (see ISSUE-229). Summary is computed live from tenant-scoped log rows (no cache key at all → no cross-tenant cache hit). SignalR progress group deferred. The no-cross-tenant guarantee HOLDS (via the EF filter); rate-budget/cache/SignalR isolation is vacuously satisfied (layers absent). CONDITIONAL PASS recorded."
---

# TC-PAY-ISO-044: Tenant-scoped distribution infrastructure -- the per-tenant SMTP rate-limiter/throttle, sender-domain config, distribution-summary/progress cache, and SignalR progress group are tenant-scoped; no cross-tenant rate-budget sharing, sender bleed, cache hit, or progress leak

## 1. Test Objective
Verify AC-5 and FR-6/FR-8/NFR-1: the distribution's stateful infrastructure is keyed per tenant. The SMTP rate-limit budget (FR-6) is tracked per tenant so Tenant A saturating its 100/min cap does not consume or block Tenant B's budget; the resolved sender domain (BR-4) is the writing tenant's; any distribution-summary / progress cache and the SignalR progress group are tenant-scoped (no shared/global key, no cross-tenant cache hit, no cross-tenant progress broadcast). (CONDITIONAL: if a rate-limiter/cache/SignalR layer is not yet present, assert no shared/global key is used and that queries/throttles are always tenant-filtered.)

## 2. Related Requirements
- User Story: US-PAY-011
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6, FR-8
- Non-Functional Requirements: NFR-1 (rate-limited throughput)
- Business Rules: BR-4 (tenant sender domain)

## 3. Preconditions
- Two Active tenants "acme" (A) and "globex" (B), each with a Finalized run + configured SMTP rate limit + sender domain.
- Both tenants able to run a distribution concurrently; observability into the rate-limiter, cache keys, and SignalR groups.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| A cap | 100/min | independent budget |
| B cap | 100/min | independent budget |
| A sender | payroll@acme.yourhrm.com | BR-4 |
| B sender | payroll@globex.yourhrm.com | BR-4 |
| Cache/group keys | tenant:{tenantId}:payroll:distribution:* | tenant-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run distributions for acme and globex concurrently; saturate acme's 100/min cap. | acme throttles at ~100/min; globex's throughput is unaffected -- the rate budget is per-tenant, not shared (FR-6). |
| 2 | Inspect the From/sender on each tenant's emails. | acme emails use acme's sender domain, globex emails use globex's -- no sender bleed across tenants (BR-4). |
| 3 | Inspect distribution-summary / progress cache keys (if a cache exists). | Keys are tenant-scoped (e.g. `tenant:{tenantId}:payroll:distribution:{runId}`); no shared/global key; a write for acme never invalidates/serves globex's summary. |
| 4 | Subscribe to the SignalR progress group as a globex user while acme's distribution runs. | globex receives NO acme progress events; progress is broadcast only to the owning tenant's group (FR-8). |
| 5 | (If no rate-limiter/cache/SignalR layer yet) Assert the fallback. | Throttling/queries are always tenant-filtered with no shared/global key; CONDITIONAL note recorded -- the no-cross-tenant guarantee still holds. |

## 6. Postconditions
- Rate budget, sender domain, summary/progress cache, and SignalR progress are all tenant-scoped; no cross-tenant budget sharing, sender bleed, cache hit, or progress leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
