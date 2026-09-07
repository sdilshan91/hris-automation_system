---
name: stale-container-before-blaming-code
description: Before reporting a runtime/code mismatch as a defect, check the running container's build date against the commit that fixed it
metadata:
  type: feedback
---

When observed runtime behaviour contradicts code that is provably in your base, **check the deployed
binary's age before filing a defect**.

**Why:** on 2026-09-07 a probation cycle appeared in the default `/performance/dashboard/trend` series even
though `GetTrendAsync` filters it. The candidate explanations offered were (1) the fix is incomplete against
real Postgres (the ISSUE-427 InMemory-masks-Postgres pattern), (2) the API-layer default differs, (3) the
seed is mislabelled. It was **none of them**: `docker image inspect hris-backend` was built
2026-09-02T03:07 and the fix (`a0621a5c`, PR #667) landed 2026-09-07T13:32 — **98 commits** after the image.
Filing it as a defect in freshly-merged code would have sent someone chasing a bug that does not exist.

**How to apply:** the three cheap commands are
`docker image inspect <img> --format '{{.Created}}'`,
`git log -1 --format='%ad' --date=iso <fix-sha>`, and
`git log --since='<image build time>' --oneline | wc -l`.
Corroborating signal: if **two independent code paths** that both exist in your base are simultaneously
absent at runtime, suspect the binary, not the code — a query-translation bug would have to break both at
once. Also note that any perf/behaviour numbers taken from a stale container must be reported with that
caveat attached.
