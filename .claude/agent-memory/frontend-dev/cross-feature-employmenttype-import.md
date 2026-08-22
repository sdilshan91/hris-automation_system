---
name: cross-feature-employmenttype-import
description: Recruitment conversion reuses Core HR EmploymentType — import path depth differs between models/ and components/<name>/
metadata:
  type: feedback
---

When a recruitment feature reuses a Core HR type (e.g. `EmploymentType` /
`EMPLOYMENT_TYPE_OPTIONS` from `core-hr/employees/models/employee.models`), the
relative import depth depends on the importing file's nesting:

- from `features/recruitment/models/*.ts` → `../../core-hr/employees/models/employee.models`
- from `features/recruitment/components/<name>/*.ts` → `../../../core-hr/employees/models/employee.models`

**Why:** US-REC-010 conversion-form (a component, 3 levels deep) and
conversion.models (2 levels deep) both import EmploymentType; I used the 2-level
path in the component first and the build failed with "Could not resolve". Easy to
miss because the models file right next door compiles fine with the shorter path.

**How to apply:** count the dirs to `features/` and add one `../` for a
`components/<name>/` file vs a `models/` file. EmploymentType is PascalCase
(Full-Time/Part-Time/Contract/Intern) per US-PLT-003 — don't redeclare it locally,
reuse the Core HR one so the enum stays single-source. See [[fe-iuser-no-employeeid]]
for the related "reuse, don't re-derive" pattern.
