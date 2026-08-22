---
name: secure-cookie-needs-https-proxy
description: Refresh cookie is always emitted Secure; session-survives-reload only works when the SPA is served over real HTTPS (nginx TLS proxy), not plain HTTP
metadata:
  type: project
---

The `refreshToken` httpOnly cookie from `POST /api/v1/auth/login` (and `/auth/refresh`) is
emitted **unconditionally** with attributes `secure; httponly; samesite=strict; path=/api/v1/auth`
— confirmed even on the plain-HTTP backend at :5000.

**Why:** over plain HTTP a browser silently DROPS a `Secure` cookie, so the refresh token was
never stored → a page reload (F5) had no cookie → bootstrap `/auth/refresh` → 401 → bounce to
`/auth/login`. Over real TLS the browser accepts/stores it, so reload bootstrap restores the
session. The server behavior didn't change; **serving the SPA over HTTPS is the fix**.

**How to apply:** when debugging "reload logs me out" / session-not-persisting on a custom domain,
check the scheme first. Local HTTPS is provided by an nginx TLS reverse proxy at
`https://acme.myhrm.org` (443, no port) routing `/api`→:5000 and `/`→ng serve :4200. Cert is a
**self-signed** `*.myhrm.org` cert → must ignore-HTTPS-errors when navigating. Cookie path is
`/api/v1/auth`, so the refresh URL `/api/v1/auth/refresh` is correctly in scope. Refresh token
**rotates** on every use (new Set-Cookie each call) — when reproducing with curl, use one jar with
both `-c` and `-b` or the rotated token is lost.
