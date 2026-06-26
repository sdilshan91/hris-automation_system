---
name: us-chr-006-org-tree-findings
description: US-CHR-006 org-tree representative pass — real route, flat acme tree, BUG-003 extends to tree READ (both views), ISSUE-023 flag quirk
metadata:
  type: project
---

US-CHR-006 (org tree / hierarchy viz) REPRESENTATIVE API pass, 2026-06-25.

Fact: real route is `GET /api/v1/tenant/org-tree?view={department|reporting}&parentId&depth&includeInactive` (the 25 TCs all say the un-prefixed `/api/v1/org-tree` — same `/tenant/` prefix drift class as the FE↔BE mismatch note). Siblings: `GET /api/v1/tenant/departments/tree`, `GET .../employees/{managerId}/direct-reports`. Validation is solid + fast-4xx: invalid view→400, depth out of 1..10→400, malformed parentId→400, bad parentId→404.

**Why:** acme's seeded org tree is FLAT — 3 root depts (engineering/Engineering/Sales, all childrenCount:0), reporting view returns ~14 employee nodes all parentId:null (no manager assignments seeded), so deep-nesting/lazy-load/reporting-hierarchy TCs can only be validated at the contract level, not with real multi-level data.

**How to apply:** For US-CHR-006 re-runs — (1) **BUG-003 confirmed on the tree READ surface, BOTH department AND reporting views** (acme token + `X-Tenant-Subdomain: techoneglobal` → 200 leaks techoneglobal's `ToneEng`/`Cross Write`); extended BUG-003's affected-surfaces list, did NOT re-file. The by-id sibling `direct-reports` correctly 404s cross-tenant (act-as-resolved-tenant contrast). (2) Filed **ISSUE-023 LOW**: `reportingViewAvailable` is view-relative (false in dept view, true in reporting view for identical data) — a UI gating the toggle on the dept-view value would hide a working feature. (3) `includeInactive=true` surfaces a dept literally named `'; DROP TABLE departments; --` (prior SQLi test residue stored harmlessly) — good evidence parameterized queries hold. Verdicts: 10 PASS / 1 FAIL (ISO-021) / 14 BLOCKED (fe-platform-bound viz + k6/CDP perf + RLS/cache env). See [[testing-loop-report-only]].
