---
name: project-flaky-portal-magiclink-test
description: PortalMagicLinkTests.Verify_TamperedSignature_IsRejected is a known non-deterministic flake — not a payroll/backend regression
metadata:
  type: project
---

`HRM.Tests.Unit.PortalMagicLinkTests.Verify_TamperedSignature_IsRejected` intermittently FAILS
("Expected ok to be False, but found True") in a full `dotnet test` run, then PASSES on every isolated
re-run.

**Why:** the test issues a fresh RANDOM token each run and tampers it by flipping the LAST base64 char
(`token[^1] == 'A' ? 'B' : 'A'`). Base64's trailing-bit ambiguity means some last-char swaps decode to
the SAME bytes, so the HMAC signature still verifies → `TryVerify` returns true. Data-dependent on the
random token, hence intermittent. (`Verify_TamperedPayload_IsRejected` flips a char BEFORE the dot and is
stable.)

**How to apply:** if a `/implement-all` verify gate fails ONLY on this test, it is NOT your story's
regression — re-run it in isolation to confirm green, and do not "fix" it from a payroll/non-auth story
(out of lane). A real fix would tamper a middle signature char or assert decode-equivalence, owned by the
auth/portal module. Seen during US-PAY-009.
</content>
