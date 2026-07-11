# QA — INSTRUCTIONS

How to work in QA.

- **Author test cases:** IEEE 829; every TC links back to a US + acceptance criteria (traceability rule). Use `@qa-engineer`.
- **Execute (report-only):** `/test-all [module|US-ID]` (loop) or `/test-us US-{ID}` (single). Pre-flight the running stack; log every defect to [`TEST-FINDINGS.md`](TEST-FINDINGS.md); flip [`TEST-STATUS.md`](TEST-STATUS.md).
- **Exploratory passes:** follow [`EXPLORATORY-QA-PLAYBOOK.md`](EXPLORATORY-QA-PLAYBOOK.md).
- **Env setup:** [`TEST-ENV-SETUP-PLAN.md`](plans/TEST-ENV-SETUP-PLAN.md). Run the backend WITHOUT the VS Code debugger for perf/availability TCs.
- **Root-cause a failing TC:** read Serilog by `RequestId` at `src/backend/HRM.Api/Logs/` before inferring from the HTTP body (`/fault-diagnosis`).
- **Close a finding:** only via `/verify-fix` after its fix PR merges.
