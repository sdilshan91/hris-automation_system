---
id: TC-ADM-ISO-028
user_story: US-ADM-010
module: Admin Console
priority: critical
type: security
status: pass
created: 2026-06-17
---

# TC-ADM-ISO-028: Cross-tenant isolation — export bundle contains ZERO rows from another tenant

## 1. Test Objective
Verify AC-5 Test Hint ("Cross-tenant isolation"): a Tenant A export produces a bundle whose every CSV and `audit_log.jsonl` contains ONLY Tenant A rows and ZERO Tenant B rows. The per-entity export queries run under the EF global query filter bound to A's `ITenantContext`, so cross-tenant rows are structurally unreachable.

## 2. Related Requirements
- User Story: US-ADM-010
- Acceptance Criteria: AC-5 (ITenantContext scoping; no cross-tenant data)
- Functional Requirements: FR-2 (per-entity query filtered by tenant_id)
- Business Rules: BR-1 (own-tenant only)
- Test Hints: "Cross-tenant isolation: ... verify the export ZIP contains zero records from Tenant B."

## 3. Preconditions
- Two tenants Acme (A) and Beta (B), each populated with overlapping entity types and KNOWN, distinguishable marker values (e.g. Beta employee "ZZ-BETA-MARKER", a Beta-only national id, Beta-only audit actions).
- Tenant Admin "Tara" authenticated at `acme.yourhrm.com` (A).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| beta_marker_employee | ZZ-BETA-MARKER | must NOT appear in A's export |
| beta_marker_audit | "BetaOnly.Action" | must NOT appear in audit_log.jsonl |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Tara, run a FULL export of Acme; let it complete | Bundle generated for Acme. |
| 2 | Grep every CSV in the bundle for Beta's marker values | Zero matches — no Beta employee/national-id/marker rows. |
| 3 | Grep `audit_log.jsonl` for Beta-only audit actions | Zero matches. |
| 4 | Sum `row_count` across the manifest and compare to Acme's known totals | Counts equal Acme-only totals (no Beta rows inflating any entity). |
| 5 | Inspect `manifest.json` `tenant_id`/`tenant_name` | = Acme only. |

## 6. Postconditions
- No state change; bundle proven free of cross-tenant data.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
