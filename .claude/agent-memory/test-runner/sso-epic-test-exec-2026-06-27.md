---
name: sso-epic-test-exec-2026-06-27
description: Enterprise SSO epic (US-AUTH-011..016) re-exec — US-AUTH-015 FE PASS clean; all API arms blocked (BE down :5000); 012/016 not built; happy-path arms need interactive Entra login
metadata:
  type: project
---

# Enterprise SSO epic (US-AUTH-011..016) test exec — 2026-06-27 (REPORT-ONLY)

Re-execution of the SSO epic. **No new findings.**

**Where SSO is tracked:** NOT in `TC-AUTH-011..016.md` — those are the OLD reset-password/MFA cases
(do NOT touch their `status:`). The SSO epic is tracked at **US-level in `TEST-STATUS.md` § "1b. Enterprise
SSO epic"**. AC-level specs live in `user-stories/authentication/US-AUTH-011..016.md` +
`SSO-EPIC-STATUS-AND-TODO.md` (the reconciled checklist; tracker STATUS.md was stale pre-PR #112).

**Built vs not (PR #112 / commit 7ea6ce6):** BUILT POC = 011 (OIDC challenge/callback/id_token in
`EntraSsoService.cs`), 013 (`CheckIsolation`+`ValidateMicrosoftIssuer` fail-closed), 014
(`AuthService.SsoSignInAsync` match/JIT, ~`AuthService.cs:1403`), 015 (FE button + sso-callback).
NOT built = **012** (per-tenant DB config; allow-list still in `appsettings EntraSsoOptions.TenantAllowList`)
and **016** (enforcement/break-glass/admin-consent) → mark `[b] not-implemented`, never invent failures.

**This pass result:**
- BE on :5000 was **DOWN** (not listening; curl exit 7 / HTTP 000 after 30s poll; FE `tenant/context` +
  challenge XHR → ERR_CONNECTION_REFUSED). So 011 challenge/callback + 013 config arms = `[b] be-down`
  (they live-PASSed 2026-06-26 — carried by reference). FE :4200 UP.
- **US-AUTH-015 PASS (clean):** button renders (MS icon + "or" divider); click → full-page redirect to
  `${apiBaseUrl}/auth/sso/challenge?returnUrl=…&tenant=…` (`login.component.ts:115`, network-confirmed);
  4 sso_error codes (`not_configured`/`not_available`/`access_denied`/`sso_failed`) each → distinct friendly
  msg in ARIA `role=alert`. `tenant=platform` when loaded on platform host (correct, not a bug).

**TCs needing interactive Microsoft login (cannot automate):** 011 AC-3/AC-4/AC-6 (code exchange→id_token,
JWT issuance+redirect, id_token negatives need mock IdP), 013 positive allow-list match, 014 match/link/JIT.
011 AC-1/2/5/7 + FR-8 are curl-automatable — only blocked because BE was down; re-run when :5000 is back.

See [[testing-loop-report-only]], [[qa-no-debugger-for-perf]].
