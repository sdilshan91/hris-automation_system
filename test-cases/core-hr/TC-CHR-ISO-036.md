---
id: TC-CHR-ISO-036
user_story: US-CHR-009
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-036: Cache keys for employee status and employment history are tenant-scoped

## 1. Test Objective
Verify that any caching of employee status data or employment history queries uses tenant-scoped cache keys, preventing Tenant A's cached data from being served to Tenant B. This validates NFR-2 for the caching layer.

## 2. Related Requirements
- User Story: US-CHR-009
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant A ("acme") has employee "John Smith" with status `suspended` and employment history entries.
- Tenant B ("globex") has employee "Jane Doe" with status `active`.
- Application caching is enabled (if implemented for status/history queries).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Employee with status: suspended |
| Tenant B | globex | Employee with status: active |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Tenant A HR Officer, fetch John Smith's profile (triggering cache population if caching is active). | Profile loads with status "suspended". Cache entry created with key containing tenant-a-uuid (e.g., `employee:tenant-a-uuid:emp-john-uuid:status`). |
| 2 | Inspect cache keys (Redis, in-memory, or distributed cache). | All cache entries for employee status and history include the tenant_id in the key. No key exists without tenant scoping. |
| 3 | As Tenant B HR Officer, fetch any employee profile. | Only Tenant B's data is returned. Cache lookup uses tenant-b-uuid-scoped keys and does NOT return Tenant A's cached data. |
| 4 | As Tenant B HR Officer, attempt to construct and query a Tenant A cache key pattern. | The application does not expose raw cache access. Even if a cache key collision were engineered, the application layer would reject the tenant mismatch. |
| 5 | If no caching is implemented for status/history data, verify the code does not use un-scoped keys in any future caching paths. | Code review confirms that any `ICacheService` or `IMemoryCache` usage for employee-related data includes tenant_id in the key. If no caching exists, mark this test as N/A with a note to verify when caching is added. |

## 6. Postconditions
- No cross-tenant data was served from cache.
- Cache key pattern verified as tenant-scoped (or N/A if no caching yet).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
