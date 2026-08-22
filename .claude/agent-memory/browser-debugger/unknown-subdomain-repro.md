---
name: unknown-subdomain-repro
description: Reproducing the "workspace does not exist" unknown-subdomain case needs a hosts entry; the not-found response is path-dependent (SPA shell on /, workspace-not-found HTML on /api/*)
metadata:
  type: project
---

Reproducing the unknown-subdomain ("This workspace does not exist") case in the local
*.myhrm.org Docker/nginx stack has two gotchas:

1. **DNS first.** `nope.myhrm.org` is NOT in `/etc/hosts` (only e2e, platform, admin, myhrm.org
   map to 127.0.0.1). A **real browser** therefore fails at DNS resolution — you never reach the
   workspace-not-found page. To repro you must either add the subdomain to hosts or, with curl,
   force it: `curl --resolve nope.myhrm.org:443:127.0.0.1 ...`.

2. **Response is path-dependent.** With the host forced to 127.0.0.1:
   - `GET /` → nginx still serves the **Angular SPA shell** (index.html, `<app-root>`, title
     "YourHRM") even for an unknown subdomain — the not-found gate does NOT fire on the static route.
   - `GET|POST /api/v1/...` → returns an HTML page `<title>Workspace not found</title>` /
     `<h1>This workspace does not exist.</h1>` — the tenant-resolution layer rejects the unknown
     subdomain at the API. So the "workspace does not exist" wall lives on the API/tenant-resolution
     path, not the SPA route.

**How to apply:** when asked to verify unknown-subdomain handling, hit an `/api/v1/*` path (not `/`)
to see the rejection, and remember a real browser needs the host in `/etc/hosts` or it dies at DNS.
Tenant "e2e" resolves to tenant_id `019ef42c-53b1-74fc-bd3b-12048e368274` (stable). See
[[secure-cookie-needs-https-proxy]] for the TLS-proxy context.
