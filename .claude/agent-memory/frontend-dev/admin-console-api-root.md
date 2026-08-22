---
name: admin-console-api-root
description: System Admin Console endpoints live at /api/admin (not /api/v1); derive by stripping /v1 from environment.apiBaseUrl
metadata:
  type: project
---

System Admin Console (admin.yourhrm.com) backend endpoints are rooted at `/api/admin/...`, NOT the tenant-scoped `/api/v1/...` namespace the rest of the app uses. `environment.apiBaseUrl` is `http://localhost:5000/api/v1`.

**Why:** The admin console is the platform/system context (cross-tenant), so its API sits outside the per-tenant `/v1` versioned namespace. US-ADM-001's contract was explicit: `POST /api/admin/tenants`, `GET /api/admin/subscription-plans`, etc.

**How to apply:** In an admin-console service derive the root with `environment.apiBaseUrl.replace(/\/v1$/, '') + '/admin'` so a single env var still drives both contexts. Mirror that in the spec when asserting URLs. The ApiResponse envelope is still stripped globally ([[fe-be-apiresponse-envelope-mismatch]] resolved by apiEnvelopeInterceptor / US-PLT-001), so services consume bare payloads. Route lives under `admin/tenants` gated by `roleGuard(['System Admin'])`.
