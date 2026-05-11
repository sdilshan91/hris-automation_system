---
id: US-AUTH-007
module: Authentication & Authorization
priority: Must Have
persona: Tenant User (all roles) / System Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-AUTH-007: Tenant resolution from subdomain

## 1. Description
**As a** platform user navigating to a tenant workspace (e.g., `acme.yourhrm.com`),
**I want** the system to automatically identify my organization from the subdomain,
**So that** I see my organization's branded experience and all data is correctly scoped to my tenant without manual selection.

## 2. Preconditions
- Wildcard DNS is configured for `*.yourhrm.com` pointing to the platform's load balancer.
- A wildcard TLS certificate covers `*.yourhrm.com`.
- The `tenant` table contains provisioned tenants with unique `subdomain` slugs.
- Redis is available for tenant lookup caching.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A user navigates to `acme.yourhrm.com` and tenant "acme" exists with status `active` | The request hits the Tenant Resolution Middleware | The middleware extracts "acme" from the host header, looks up the tenant (Redis cache -> DB fallback), populates `ITenantContext` with the tenant's ID, subdomain, status, plan, and enabled modules, and the request proceeds to the controller. |
| AC-2 | A user navigates to `unknown.yourhrm.com` and no tenant with subdomain "unknown" exists | The middleware attempts resolution | The middleware returns HTTP 404 Not Found with a static error page; no SPA shell, login form, or API endpoints are exposed. |
| AC-3 | A user navigates to a reserved subdomain (e.g., `www.yourhrm.com`, `api.yourhrm.com`) | The middleware checks against the reserved list | The request is routed to the appropriate system handler (marketing site, API docs, etc.) and does NOT enter the tenant resolution flow. |
| AC-4 | A user navigates to `admin.yourhrm.com` | The middleware resolves the system tenant | The `ITenantContext` is populated with `IsSystemContext = true`, enabling cross-tenant System Admin operations for authorized users only. |
| AC-5 | A tenant's status is `suspended` | A user navigates to the tenant's subdomain | The middleware resolves the tenant, sets `ITenantContext.Status = Suspended`, and downstream middleware renders a suspension notice page with the reason; login is blocked (except for tenant admin viewing suspension details). |
| AC-6 | The Redis cache entry for a tenant expires or is missing | A request arrives for that tenant | The middleware falls back to a PostgreSQL query, populates the cache with a configurable TTL (e.g., 5 minutes), and continues processing. The cache miss adds <= 50 ms latency. |

## 4. Functional Requirements
- FR-1: Tenant Resolution Middleware SHALL execute early in the ASP.NET Core middleware pipeline, before authentication and authorization.
- FR-2: The middleware SHALL extract the subdomain from the `Host` header by stripping the platform's base domain.
- FR-3: The middleware SHALL check the subdomain against a reserved list (`www`, `api`, `admin`, `app`, `mail`, `status`, `docs`, `help`, `support`, `static`, `cdn`, `dev`, `stage`, `prod`, `test`, `qa`) and route accordingly.
- FR-4: For `admin.yourhrm.com`, the middleware SHALL set `ITenantContext.IsSystemContext = true`.
- FR-5: For regular subdomains, the middleware SHALL look up the tenant: first in Redis (`t:subdomain:{slug}` key), then in PostgreSQL if cache miss.
- FR-6: The resolved tenant data SHALL populate `ITenantContext` (scoped DI service): `TenantId`, `Subdomain`, `Status`, `Plan`, `IsSystemContext`, `EnabledModules`.
- FR-7: If tenant is not found, the middleware SHALL short-circuit with 404 and a static error page.
- FR-8: If tenant is found but in a non-accessible state, the middleware SHALL set the context and allow downstream middleware to handle state-specific behavior (suspension page, read-only mode, etc.).
- FR-9: The tenant lookup result SHALL be cached in Redis with a configurable TTL (default: 5 minutes) and invalidated on tenant status changes.
- FR-10: Every log record produced after tenant resolution SHALL include `tenant_id` via Serilog enricher.

## 5. Non-Functional Requirements
- NFR-1: Tenant resolution overhead SHALL be <= 5 ms when the tenant is cached in Redis.
- NFR-2: Cache miss resolution (DB lookup + cache write) SHALL be <= 50 ms at P95.
- NFR-3: The middleware SHALL handle Redis unavailability gracefully by falling back to direct DB lookup without failing the request.
- NFR-4: Subdomain validation SHALL reject invalid characters (only lowercase alphanumeric and hyphens allowed, 3-63 characters, no leading/trailing hyphens).
- NFR-5: The static 404 page for unprovisioned subdomains SHALL NOT leak any platform information (no SPA bundle, no API endpoints).

## 6. Business Rules
- BR-1: Each tenant has a unique subdomain slug that is immutable after provisioning (Phase 1).
- BR-2: Reserved subdomains cannot be claimed by any tenant.
- BR-3: The system tenant (`admin.yourhrm.com`) is a special case that cannot be suspended or terminated through normal flows.
- BR-4: Tenant resolution is required for all requests except health check endpoints and system-level API routes.
- BR-5: Custom domains (`hr.acme-corp.com`) are deferred to Phase 2; the architecture supports adding a `tenant_custom_domain` lookup table later.

## 7. Data Requirements
- **`tenant` table fields used:** `tenant_id`, `subdomain` (unique, indexed), `status`, `plan_id`, `name`, `logo_url`, `primary_color`.
- **Redis cache key:** `t:subdomain:{slug}` -> serialized tenant resolution DTO (ID, status, plan, enabled modules).
- **`ITenantContext` interface:** `TenantId` (Guid), `Subdomain` (string), `Status` (enum), `Plan` (enum/object), `IsSystemContext` (bool), `EnabledModules` (IReadOnlyCollection<string>).
- **Reserved subdomain list:** maintained in configuration/constants.

## 8. UI/UX Notes
- Notion-like experience: the browser URL always shows the tenant subdomain (e.g., `acme.yourhrm.com/dashboard`).
- The login page and app shell display tenant branding (logo, primary color) resolved from `ITenantContext`.
- For unprovisioned subdomains: a clean, minimal 404 page with the platform logo and "This workspace does not exist" message, plus a link to the main platform site.
- For suspended tenants: a branded page showing the tenant name, suspension reason (if available), and a contact support link.
- No tenant selector dropdown in the URL bar; tenant is always determined by subdomain.

## 9. Dependencies
- Infrastructure: Wildcard DNS and wildcard TLS certificate for `*.yourhrm.com`.
- Redis for caching (with graceful fallback to DB).
- PostgreSQL `tenant` table with subdomain index.
- Tenant provisioning (System Admin module) for creating tenant records.

## 10. Assumptions & Constraints
- The platform base domain (`yourhrm.com`) is configurable via application settings.
- All tenants share the same base domain in Phase 1; custom domains are deferred.
- The middleware must be stateless and work correctly behind a load balancer with multiple API instances.
- Subdomain extraction logic must handle cases where the Host header includes a port number (e.g., `acme.localhost:5001` in development).

## 11. Test Hints
- **Happy path:** Request to `acme.yourhrm.com`; verify `ITenantContext.TenantId` is correctly set and subsequent queries are scoped.
- **Unknown subdomain:** Request to `nonexistent.yourhrm.com`; verify 404 static page.
- **Reserved subdomain:** Request to `www.yourhrm.com`; verify no tenant resolution, correct routing.
- **System tenant:** Request to `admin.yourhrm.com`; verify `IsSystemContext = true`.
- **Suspended tenant:** Verify suspension page is rendered, login blocked.
- **Cache hit:** Verify resolution in <= 5 ms from Redis.
- **Cache miss/fallback:** Disable Redis; verify DB fallback works and response is still returned.
- **Cache invalidation:** Change tenant status; verify cached entry is invalidated and next request picks up new status.
- **Invalid subdomain format:** Request with `ACME.yourhrm.com` (uppercase) or `a.yourhrm.com` (too short); verify proper handling.
- **Cross-tenant isolation:** Verify that resolving tenant A does not expose any data from tenant B.
- **Local development:** Verify `acme.localhost:5001` resolves correctly in dev environment.
