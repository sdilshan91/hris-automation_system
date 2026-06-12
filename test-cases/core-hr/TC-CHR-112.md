---
id: TC-CHR-112
user_story: US-CHR-002
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-112: Editing at field length limits -- boundary test for profile fields

## 1. Test Objective
Verify that profile fields correctly handle boundary values at maximum length limits. Specifically: first_name at max 100 characters, address at max length, and phone at max length. Also verify that exceeding the limit is rejected. This validates FR-2, FR-3.

## 2. Related Requirements
- User Story: US-CHR-002
- Acceptance Criteria: AC-2 (save succeeds with valid data)
- Functional Requirements: FR-2, FR-3
- Data Requirements: Field length constraints

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme" tenant.
- Employee "Jane Doe" exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Employee ID | {jane_doe_id} | UUID |
| Max-length first_name | "A" repeated 100 times | 100 chars (boundary) |
| Over-limit first_name | "A" repeated 101 times | 101 chars (exceeds) |
| Max-length address | "B" repeated 500 times | 500 chars (assumed max) |
| Empty phone | "" | Empty string boundary |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `PATCH /api/v1/tenant/employees/{jane_doe_id}` with `{ "first_name": "A...A" (100 chars), "xmin": "..." }` | Response is 200 OK. first_name is updated to the 100-character string. |
| 2 | Verify database | first_name column contains exactly 100 "A" characters. |
| 3 | Send `PATCH /api/v1/tenant/employees/{jane_doe_id}` with `{ "first_name": "A...A" (101 chars), "xmin": "..." }` | Response is 400 Bad Request. Validation error indicates first_name exceeds maximum length. |
| 4 | Verify database | first_name remains the 100-character value from step 1. |
| 5 | Send `PATCH /api/v1/tenant/employees/{jane_doe_id}` with `{ "address": "B...B" (500 chars), "xmin": "..." }` | Response is 200 OK (if 500 is within limit) or 400 (if it exceeds). Record the actual max and verify boundary. |
| 6 | Send `PATCH /api/v1/tenant/employees/{jane_doe_id}` with `{ "phone": "", "xmin": "..." }` | Response depends on whether phone is nullable. If nullable: 200 OK and phone is cleared. If required: 400 Bad Request with validation error. |
| 7 | Test whitespace-only value: `PATCH` with `{ "first_name": "   ", "xmin": "..." }` | Response is 400 Bad Request. Validation should reject whitespace-only strings for required fields. |

## 6. Postconditions
- Field values at exact boundary are accepted and persisted.
- Values exceeding the boundary are rejected with clear validation messages.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
