---
name: test-guard-pending-keyword
description: test-integrity-guard blocks spec Writes containing the bare word "pending" (Jasmine pending() marker regex); avoid it in signal/var names referenced from specs
metadata:
  type: feedback
---

The `test-integrity-guard` PreToolUse hook blocks any `*.spec.ts` Write whose text
contains the token `pending` — it treats it as the Jasmine `pending()` skip marker,
even when it's just a component property like `component.pending()`.

**Why:** the guard's skip-marker regex matches `pending` as a whole word; it can't
tell a real `pending()` skip call from an identifier named `pending`.

**How to apply:** when a component exposes state a spec must read, don't name it
`pending`. Name it `pendingAction` / `pendingX` instead (also clearer). If you hit the
block on a legitimate identifier, rename rather than setting
`CLAUDE_DISABLE_TEST_GUARD=1`. Seen on US-ADM-007 workflow-list confirm-dialog signal.
Related: [[routerlink-breaks-sibling-spec]].
