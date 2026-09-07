---
name: inert-guard-needs-a-caller-with-pending-work
description: A dry-run/no-op guard around SaveChangesAsync is unobservable unless the CALLER has pending changes staged — assert on the caller's staged row, not on the feature's own rows
metadata:
  type: feedback
---

When a "preview"/dry-run path guards `SaveChangesAsync` with `if (!dryRun)`, deleting that guard is a
**surviving mutant** against any test that only checks the feature's own rows: if the dry run also skipped
its `Add`/tracked-graph mutations, `SaveChangesAsync` on a clean context is a silent no-op and every
assertion stays green.

The guard IS observable, but only through one channel: an unrelated entity the **caller** staged in the
same scope and has not saved yet. An unguarded flush inside a preview commits *their* work.

**Why:** ENH-015 (`RecommendationService.AutoGenerateAsync`). Removing the SaveChanges guard passed 3/3
arms. Only after adding an arm that stages a `RecommendationBudget` in the scoped context *without saving*
and then asserts it was NOT persisted did the mutant die. See
[[feedback-guards-must-be-mutation-proven]] and [[feedback-mutation-check-revert-before-report]].

**How to apply:** For any dry-run / preview / no-op-path guard around a flush, write the arm as
"caller stages X, calls the preview, X must still be unpersisted". Asserting only that the *feature's*
rows are absent proves nothing about the flush. Two distinct traps live here and need two distinct arms:
(1) the tracked-graph mutation (e.g. an audit-event append that runs while the parent is still Detached and
so gets `Add`ed on its own), and (2) the flush itself.
