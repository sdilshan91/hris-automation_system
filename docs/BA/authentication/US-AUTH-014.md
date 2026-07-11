---
id: US-AUTH-014
module: Authentication & Authorization
priority: Should Have
persona: Tenant User (all roles) / Tenant Admin
status: draft
created: 2026-06-21
sprint: backlog
acceptance_criteria_count: 7
---

# US-AUTH-014: User matching, account linking & JIT provisioning

## 1. Description
**As a** tenant user signing in with Microsoft for the first time,
**I want** the platform to recognize my existing HRM membership by my corporate email and link my Microsoft identity to it,
**So that** I land in my existing account with my existing roles instead of a duplicate or a dead end.

**As a** tenant admin,
**I want** the option to just-in-time provision a new membership for allow-listed users who don't yet have one,
**So that** onboarding new employees via Microsoft SSO is frictionless without pre-creating every account.

## 2. Preconditions
- Isolation (US-AUTH-013) has already permitted the login for the resolved tenant.
- A validated `id_token` provides `oid`, verified `email`, and `name`.
- The tenant's SSO config (US-AUTH-012) specifies `jit_enabled` and `jit_default_role`.
- The user/membership model (`user_tenant`) and RBAC (US-AUTH-006) are available.

## 3. Acceptance Criteria
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A user has a `user_tenant` membership previously linked to their Entra `oid` | They sign in with Microsoft | The system matches by `oid`, loads the existing membership + roles, and issues the app JWT for that account — no duplicate is created. |
| AC-2 | A user has a membership in the resolved tenant by **verified email** but no linked `oid` yet | They sign in with Microsoft for the first time | The system matches by email, **links** the Entra `oid` to that membership (stored for future logins), and issues the app JWT. |
| AC-3 | The Entra email matches a membership in a **different** tenant only (none in the resolved tenant) and JIT is disabled | They sign in with Microsoft | The system does **not** grant access to the resolved tenant and does **not** cross-link; it returns a "no membership in this workspace" error. |
| AC-4 | No matching membership exists in the resolved tenant, JIT is **enabled**, and the user passed isolation (allow-listed domain/`tid`) | They sign in with Microsoft | The system provisions a new `user_tenant` membership with `jit_default_role`, links the `oid`, logs an `sso_jit_provisioned` audit event, and issues the app JWT. |
| AC-5 | No matching membership exists and JIT is **disabled** | They sign in with Microsoft | The system rejects the login with a "your administrator hasn't granted you access" message and logs `sso_no_membership`; no account is created. |
| AC-6 | An Entra `oid` is already linked to a membership and a different verified email now arrives for the same `oid` | They sign in | The system trusts the `oid` link as authoritative, optionally updates the stored email, and does not create a second account. |
| AC-7 | A matched/linked user's membership is suspended or the tenant is suspended | They sign in with Microsoft | The system refuses to issue a token (same suspension rules as local login) and audits the blocked attempt. |

## 4. Functional Requirements
- FR-1: The system SHALL match an SSO login to a `user_tenant` membership in the **resolved tenant** in priority order: (1) linked Entra `oid`, then (2) verified email.
- FR-2: On first successful email match, the system SHALL persist the Entra `oid` link on the membership/user for future `oid`-based matching.
- FR-3: When no membership exists and `jit_enabled` is true, the system SHALL create a new membership in the resolved tenant with `jit_default_role` and link the `oid`.
- FR-4: When no membership exists and `jit_enabled` is false, the system SHALL reject the login without creating any record.
- FR-5: Matching SHALL be strictly scoped to the resolved tenant; a membership in another tenant SHALL NOT grant access to the resolved tenant and SHALL NOT be auto-linked across tenants.
- FR-6: JIT provisioning SHALL only occur for logins that already passed isolation (US-AUTH-013); a JIT user is by definition allow-listed.
- FR-7: All matching/linking/JIT outcomes SHALL be audited (`sso_account_linked`, `sso_jit_provisioned`, `sso_no_membership`).
- FR-8: Suspended memberships/tenants SHALL block SSO token issuance identically to local login.

## 5. Non-Functional Requirements
- NFR-1: The `oid` lookup SHALL be backed by an indexed column for O(1) matching at login time.
- NFR-2: Linking and JIT provisioning SHALL be transactional — a partial failure SHALL NOT leave a half-created membership or an orphaned link.
- NFR-3: Concurrent first-logins for the same user (double-click / two tabs) SHALL NOT create duplicate memberships (idempotent upsert / unique constraint on `(tenant_id, oid)`).
- NFR-4: JIT provisioning of a single user SHALL complete within the 2-second callback budget (US-AUTH-011 NFR-4).

## 6. Business Rules
- BR-1: The Entra `oid` is the durable identity link; email is the bootstrap matcher and may change over time.
- BR-2: A user may have memberships in multiple tenants; each tenant's SSO login is matched/linked independently and never leaks roles across tenants.
- BR-3: JIT provisioning is opt-in per tenant and only ever assigns the configured non-privileged `jit_default_role` (US-AUTH-012 BR-5).
- BR-4: SSO never elevates an existing user's roles; it only authenticates. Role changes remain an explicit admin action (US-AUTH-006 / US-ADM-005).
- BR-5: A `(tenant_id, oid)` pair is unique — one Microsoft identity maps to at most one membership per tenant.
- BR-6: Email used for matching must be the **verified** claim (consistent with US-AUTH-013 BR-5).

## 7. Data Requirements
- **`user_tenant` (or user) new field:** `entra_oid` (string/GUID, nullable, indexed; unique within tenant scope).
- **Optional:** `auth_method`/`linked_at` metadata on the membership for diagnostics.
- **Consumed claims:** `oid`, verified `email`, `name`.
- **Consumed config:** `jit_enabled`, `jit_default_role` (US-AUTH-012).
- **Audit records:** `sso_account_linked` (user, tenant, oid), `sso_jit_provisioned` (user, tenant, role, oid), `sso_no_membership` (email, tenant).

## 8. UI/UX Notes
- First-time linked login is invisible to the user — they simply land in their account.
- No-membership rejection: "You don't have access to this workspace yet. Ask your administrator to invite you." (No account-existence enumeration across tenants.)
- JIT-provisioned users land on a minimal first-run state appropriate to `jit_default_role` (e.g. employee self-service), with profile completion prompts handled by existing onboarding flows where relevant.
- Tenant Admin > Users: JIT-created users are flagged with their origin ("Provisioned via Microsoft SSO") and linked Entra identity for transparency.

## 9. Dependencies
- US-AUTH-013 (isolation) — must pass before matching/JIT runs.
- US-AUTH-012 (config) — supplies `jit_enabled` / `jit_default_role`.
- US-AUTH-006 (RBAC) — role assignment for JIT and existing role loading.
- US-ADM-005 (user/role management) — admins manage and re-role provisioned users.
- US-AUTH-002 (JWT) — token issuance after a successful match.
- US-NTF-004 (audit) — link/JIT auditing.

## 10. Assumptions & Constraints
- `oid` is globally unique and stable per Microsoft identity; it is the correct durable join key.
- Email-based bootstrap matching assumes the corporate email on the HRM membership equals the verified Entra email; mismatches require an admin invite/link rather than auto-JIT.
- v1 assigns a single default role on JIT; group→role mapping is out of scope (CR-AUTH-001 §6).
- Cross-tenant identity linking is explicitly disallowed to preserve isolation; the same person in two tenants has two independent linked memberships.

## 11. Test Hints
- **oid match:** Pre-link an `oid`; sign in; assert existing account loaded, no duplicate.
- **email bootstrap + link:** Membership by email, no `oid`; first SSO login links `oid`; second login matches by `oid`.
- **Other-tenant only, JIT off:** Email exists only in tenant B; sign in to tenant A; assert reject + no cross-link.
- **JIT on:** Allow-listed user, no membership, JIT enabled; assert membership created with default role + `sso_jit_provisioned`.
- **JIT off:** Same but JIT disabled; assert reject + `sso_no_membership`, no record created.
- **Concurrency:** Fire two simultaneous first-logins; assert exactly one membership (unique `(tenant_id, oid)`).
- **Suspended:** Suspend membership/tenant; assert SSO token refused.
- **No role elevation:** Existing low-privilege user logs in via SSO; assert roles unchanged.
- **Transactional rollback:** Force a failure mid-JIT; assert no orphaned membership/link.
