---
name: perf-harness-rate-limit-ceiling
description: The k6 perf harness is capped by a global 300 req/min per-(tenant,user) limiter; any script above ~5 req/s measures 429s, not the endpoint
metadata:
  type: project
---

`Program.cs` installs a **global fixed-window rate limiter: `PermitLimit = 300`, `Window = 1 min`,
`QueueLimit = 0`**, partitioned by **(tenantId, userId)**. The `perf` tenant seeds exactly **one** user
(`perfadmin@perf.test`), so every VU in a k6 script shares **one partition**.

Measured 2026-09-07: `06-performance-dashboard.js` at 20 VU sent **26,182 requests in 3 min and exactly
901 succeeded** (300 x 3 windows). The rejected requests return 429 in ~2ms, which **drags p95 DOWN** — the
script printed a green `p(95)<2500` that was the p95 of being rate-limited. A green threshold here is the
dangerous outcome, not the red one.

**Why:** the limiter is a deliberate abuse backstop ("thousands per minute"), not a quota, and it is real
production behaviour — the harness, not the app, is what needs sizing.

**How to apply:** size any perf script to stay under 300 req/min on one login (~5 req/s). For
`06-performance-dashboard.js` that is 5 VU x ~5.1 req x one iteration per ~6.6s ~= 234 req/min. Always check
`http_req_failed` and `checks_succeeded` BEFORE reading any p95 — if `checks_succeeded` is near a multiple of
300 per minute of runtime, you measured the limiter. To load beyond 300/min, seed extra perf users so the
partition key varies. See [[perf-volume-seed-conventions]].

**Suspect by the same mechanism (unverified):** `03-scale-reads.js` (30 VU) and `05-module-lists.js`
(50 VU, 6 GETs + `sleep(0.5)`) both drive far above 300/min on one login.
