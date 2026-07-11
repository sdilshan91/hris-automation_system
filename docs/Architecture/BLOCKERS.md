# Architecture — BLOCKERS

- **RLS is not yet enabled in prod** — committed OFF by design; flip requires the 3b pre-prod checklist (CI RLS service-container job; long-running by-id jobs holding one GUC tx per batch; service-body DI-scope audit). See [`PLANS.md`](PLANS.md).
- Architecture risks surfaced by `/advisor` land in [`advisory-reports/`](advisory-reports/); security exposure in [`security-reviews/`](security-reviews/).
