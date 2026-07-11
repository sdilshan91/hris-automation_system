# Frontend — BLOCKERS

- **FE↔BE contract drift class** — paginated `{items,totalCount}` consumed as bare array, `/tenant/` prefix omissions, missing endpoints the FE already calls (e.g. `/custom-fields/active`). Fix at the service boundary. Detail in [`../QA/TEST-FINDINGS.md`](../QA/TEST-FINDINGS.md).
- **a11y systemic classes** — hand-rolled overlays missing focus-trap/inert/escape (BUG-109), role-misuse tablist (BUG-110), contrast (BUG-096). Re-test after fixes land.
- Full open list → filter [`../QA/TEST-FINDINGS.md`](../QA/TEST-FINDINGS.md) for FE layer.
