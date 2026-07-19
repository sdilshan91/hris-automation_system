---
id: TC-AUTH-114
user_story: US-AUTH-010
module: Authentication
priority: high
type: integration
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - ISSUE-063
---

# TC-AUTH-114: Account-lockout email is branded with the resolved tenant name; degrades gracefully when the tenant is unknown; AuthService plumbs the login-time tenant name into the enqueued job (ISSUE-063 — FR-8)

## 1. Test Objective
Verify the ISSUE-063 fix on US-AUTH-010 FR-8: the account-lockout notification email is **branded with the tenant name** when it is resolved, **degrades gracefully** (no broken/`TODO` content) when the tenant name is `null` (e.g. a cross-tenant login whose tenant could not be resolved), and that `AuthService` actually plumbs the resolved login-time `Tenant.Name` into the Hangfire-enqueued lockout job — content-building alone is not enough.

## 2. Related Requirements
- User Story: US-AUTH-010
- Functional Requirement: FR-8 (lockout notification email to the affected user)
- Finding: ISSUE-063 (PR #371)

## 3. Preconditions
- `LockoutNotificationService.BuildContent` callable in isolation (pure content builder).
- An auth service with a fake `IBackgroundJobClient` and a seeded tenant/user with a lockout policy (mirrors `AccountLockoutTests`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| tenantName (present) | "Acme Corporation" | must appear in the body |
| tenantName (null) | null | must not break the email |
| maxFailedAttempts | 3 | lockout threshold |
| lockoutDurationMinutes | 10 | drives LockedUntil |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | `BuildContent(..., tenantName: "Acme Corporation")`. | The email body contains "Acme Corporation" (FR-8 branding). | `LockoutNotificationContentTests.BuildContent_BrandsWithTenantName_WhenPresent` |
| 2 | `BuildContent(..., tenantName: null)`. | Subject is non-empty; body still greets the user and contains no `TODO`/broken placeholder. | `LockoutNotificationContentTests.BuildContent_DegradesGracefully_WhenTenantNameNull` |
| 3 | Fail login to the lockout threshold. | Account locked (`LockedUntil` set); `account_locked` audit written; a lockout job is enqueued via Hangfire carrying the **resolved** tenant name (not null) — proving `AuthService` plumbs `Tenant.Name` in. | `AccountLockoutTests.LoginAsync_ReachesMaxFailedAttempts_LocksAccount` |

## 6. Postconditions
- Locked-out users receive a tenant-branded email; unresolved-tenant logins still get a valid email; the enqueued job carries the login-time tenant identity.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test (null tenant name)
- [x] Boundary test (threshold reached)
- [x] Security test (lockout path)
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite):**
  - `LockoutNotificationContentTests.BuildContent_BrandsWithTenantName_WhenPresent`
  - `LockoutNotificationContentTests.BuildContent_DegradesGracefully_WhenTenantNameNull`
  - `AccountLockoutTests.LoginAsync_ReachesMaxFailedAttempts_LocksAccount`
- Backing suite trait: `[Trait("TC", "TC-AUTH-114")]`.
