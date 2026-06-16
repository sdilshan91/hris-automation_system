---
name: ef-child-collection-replace
description: Replacing a tracked child collection — RemoveRange via DbSet, never reassign/mutate the loaded navigation
metadata:
  type: feedback
---

When "replace the child rows of a tracked parent" (e.g. cycle phases, scorecard ratings), remove the old
rows and add the new ones via the **DbSet** and do NOT reassign or clear the parent's loaded navigation
collection (`parent.Children = newList` / `parent.Children.Clear()`).

**Why:** mutating the navigation while the old children are pending-delete leaves them referenced by the
parent relationship; EF's cascade fixup then resurrects a deleted child as a *modified* row on
`SaveChanges`, which surfaces as `DbUpdateConcurrencyException: Attempted to update or delete an entity that
does not exist in the store`. Found this as a real bug in `AppraisalCycleService.UpdateAsync` (US-PRF-004) —
every cycle edit threw. The established safe pattern in `ScorecardService`/`InterviewService` does DbSet
`RemoveRange` + DbSet `Add` and never touches the navigation.

**How to apply:** if you also need the new children for a side computation (e.g. syncing legacy window
columns), read them from the freshly built local list, not from the navigation — add an overload that takes
an explicit `IEnumerable<TChild>` rather than reading `this.Children`. The newly Added children fix up into
the navigation automatically via their FK.
