---
name: employee-typeahead-reuse
description: reuse Core HR EmployeeService.searchActiveEmployees for an employee typeahead in other features; debounce + min-2-char in the component
metadata:
  type: feedback
---

For an employee picker / typeahead in any feature, reuse the Core HR
`EmployeeService.searchActiveEmployees(search, pageSize)` rather than adding a new
search endpoint. It hits the directory endpoint with `statuses=active` and returns
`IPaginatedResponse<IEmployee>` (read `.data`).

**Why:** the directory search already exists and is tenant-scoped; a new per-feature
endpoint duplicates it. Used for the US-PAY-007 adjustment-form employee select and
the manager autocomplete it was originally built for.

**How to apply:** import cross-feature from a `components/<name>/` dir as
`../../../core-hr/employees/services/employee.service` and the model as
`../../../core-hr/employees/models/employee.models` (see
[[cross-feature-employmenttype-import]] for the depth rule). Put the debounce
(`debounceTime(250)`, `distinctUntilChanged`, `switchMap`) and the min-2-char guard
in the COMPONENT via a `Subject<string>`, not the service. In specs, stub
`searchActiveEmployees` to return `of({ data: [...], total, page, pageSize })` and
drive it with `fakeAsync` + `tick(250)`.
