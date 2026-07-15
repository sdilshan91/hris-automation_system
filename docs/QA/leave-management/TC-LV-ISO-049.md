---
id: TC-LV-ISO-049
user_story: US-LV-002
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-07-15
---

# TC-LV-ISO-049: Multi-tenant isolation — a cross-tenant LeaveEntitlementRule.LocationId never resolves (US-LV-002 / US-ATT-011 AC-3)

## 1. Test Objective
Verify spec §7.1 and Critical Rule #1 for the new `LeaveEntitlementRule.LocationId` FK: a leave entitlement rule whose `LocationId` points at **another tenant's Location** is never accepted and never resolves — a Tenant A employee's entitlement is never computed from a rule bound to a Tenant B Location. Targets **real Postgres** (EF query filter / RLS), not InMemory.

## 2. Related Requirements
- User Story: US-LV-002 (leave entitlement rules) — location tier added per US-ATT-011 AC-3 / D5
- Spec §7.1: `LeaveEntitlementRule.LocationId` same-tenant; null = tenant-wide
- NFR (US-ATT-011 NFR-2): cross-entity FK isolation
- Critical Rule #1

## 3. Preconditions
- Tenant A with a leave entitlement rule; Tenant B with a Location `locB`.
- Two-tenant Postgres integration setup.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A rule | entitlement rule | consumer |
| Tenant B | Location `locB` | foreign FK target |
| null LocationId | tenant-wide rule | precedence baseline |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In Tenant A's context, set/create a `LeaveEntitlementRule` with `LocationId = locB.Id`. | Rejected — `locB` not found under Tenant A's query filter; rule not persisted with a foreign location. |
| 2 | Resolve entitlement for a Tenant A employee. | Uses only Tenant A rules; a Tenant B location-scoped rule never contributes. |
| 3 | Confirm a null-`LocationId` rule resolves tenant-wide by the documented precedence (Location override > tenant-wide). | Precedence total and tenant-scoped. |

## 6. Postconditions
- No cross-tenant entitlement rule resolves; the location tier is strictly tenant-isolated.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
