---
name: formarray-toSignal-sum-gate
description: live-validate a FormArray aggregate (e.g. weights sum to 100%) by bridging valueChanges to a signal, then computed() for the submit gate
metadata:
  type: feedback
---

To gate a submit button on a cross-row aggregate of a reactive `FormArray`
(US-PRF-001: goal weights must sum to exactly 100%), bridge the array to a signal
and derive everything with `computed()`:

```ts
private readonly goalsValue = toSignal(
  this.goals.valueChanges.pipe(startWith(this.goals.value)),
  { initialValue: this.goals.value as IGoalInput[] },
);
readonly totalIs100 = computed(() => weightsTotalCorrect(...this.goalsValue()...));
readonly canSave = computed(() => this.windowOpen() && this.totalIs100() && this.form.valid && ...);
```

**Why:** OnPush + signals; `form.valid` alone can't express "weights total 100%",
and you want the bar + error + button to update live as any row's weight changes.
`startWith(this.goals.value)` seeds the signal so the gate is correct before the
first edit. Keep the sum rule a **pure exported helper** (`weightsTotalCorrect`) so
it's unit-tested without a component and reused by the template bar.

**How to apply:** any multi-row form with a total/aggregate constraint. Field
initializers run top-to-bottom, so declare `form` BEFORE the `toSignal` field (the
`get goals()` getter it calls needs `form` assigned). Gotcha: Angular omits
**disabled** controls from `FormArray.value`, so when you `form.disable()` for a
read-only state (closed window, AC-5) the aggregate reads 0 — fine if the submit
gate is already short-circuited by a separate `windowOpen()` signal, but don't rely
on the aggregate for display while disabled.
