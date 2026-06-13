---
id: TC-CHR-324
user_story: US-CHR-012
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-CHR-324: Same custom field name allowed in different tenants

## 1. Test Objective
Verify that different tenants can create custom fields with the same name for the same entity type. The uniqueness constraint on field names is scoped to tenant + entity, not globally. This validates BR-2 and provides a positive cross-tenant check complementing the ISO tests.

## 2. Related Requirements
- User Story: US-CHR-012
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both exist with status `active`.
- Tenant Admin users exist in both tenants.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme.yourhrm.com | First tenant |
| Tenant B | globex.yourhrm.com | Second tenant |
| Field Name | T-Shirt Size | Same name in both tenants |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin in "acme". Create custom field "T-Shirt Size" (dropdown). | Created successfully. |
| 2 | Authenticate as Tenant Admin in "globex". Create custom field "T-Shirt Size" (dropdown). | Created successfully -- no conflict with acme's field of the same name. |
| 3 | Query acme's custom fields. | Returns only acme's "T-Shirt Size" definition with acme's tenant_id. |
| 4 | Query globex's custom fields. | Returns only globex's "T-Shirt Size" definition with globex's tenant_id. |
| 5 | Verify the two definitions have different UUIDs and different tenant_ids. | Confirmed -- they are separate records. |

## 6. Postconditions
- Both tenants have independent "T-Shirt Size" custom field definitions.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
