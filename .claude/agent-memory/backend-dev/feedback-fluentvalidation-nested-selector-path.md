---
name: fluentvalidation-nested-selector-path
description: TestValidate property-name path when a validator rule is built via a nested-record selector lambda
metadata:
  type: feedback
---

When a FluentValidation rule is registered through a selector that dives into a nested record — e.g.
`v.RuleFor(x => selector(x).EnabledModules)` where `selector` returns a sub-record like `command.Fields` — the
error's property path is the **bare leaf name** (`EnabledModules`, `PriceMonthly`, `Currency`), NOT the dotted
parent path (`Fields.EnabledModules`).

**Why:** FluentValidation derives the property name from the final member expression it can see in the lambda; the
intermediate `selector(x)` call is opaque to it, so the parent segment is dropped.

**How to apply:** in `*ValidatorTests` use `ShouldHaveValidationErrorFor("EnabledModules")`, not
`"Fields.EnabledModules"`. Seen on the US-ADM-009 SubscriptionPlan validators, which share rules across
Create/Update via a `PlanFieldRules.ApplyEditableRules(this, x => x.Fields)` helper. The runtime rule still works
correctly; only the test assertion string was wrong.
