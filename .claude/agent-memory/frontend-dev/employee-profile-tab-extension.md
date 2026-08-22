---
name: employee-profile-tab-extension
description: Adding a tab to Core HR employee-profile — child-component pattern, scoped utility classes, and the sectionList.length spec assertion it breaks
metadata:
  type: feedback
---

When extending the Core HR employee-profile (`features/core-hr/employees/components/employee-profile/`)
with a new tab, follow the established child-component pattern and watch two gotchas.

**Why:** US-PAY-002 added a "Compensation" tab; doing it as a self-contained child
component kept the 2467-line profile diff to ~4 small edits, and two non-obvious
things broke/needed care.

**How to apply:**
- Embed a self-contained child component with `[employeeId]="employeeId"` (same as
  `app-employee-documents` / `app-employee-leave-overrides`), guarded by
  `@if (activeTab() === N)`. The child owns all feature logic + its own service.
  Edits to the profile: add the import, add to `imports:[]`, append to
  `sectionList`, add the `@if` block. Cross-feature import depth from
  `employee-profile/` to a sibling feature is `../../../../<feature>/...`.
- The profile's shared utility classes (`badge`, `skeleton-line`, `btn-spinner`,
  `tab-btn`, `section-header`) are declared in the profile's OWN component
  `styles` block, NOT global `styles.scss` — so they do NOT reach a child
  component (ViewEncapsulation). Re-declare the few you use in the child's
  `styles`. (Global classes that DO reach children: `card-notion`, `input-notion`,
  `label-notion`, `btn-primary`, `btn-secondary`, `page-container`.)
- The spec `employee-profile.component.spec.ts` has a test asserting
  `component.sectionList.length` against a hardcoded number ("should show all N
  section tabs"). Any new tab bumps it — update the count + its title. This is a
  legitimate reconciliation, not a weakening. See [[spec-default-month-signal]]
  for the general "assert against the live value, not a literal" theme.
