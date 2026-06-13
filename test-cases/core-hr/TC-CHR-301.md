---
id: TC-CHR-301
user_story: US-CHR-012
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-301: Number field rejects non-numeric value "abc" -- type validation

## 1. Test Objective
Verify that when a Number-type custom field is defined and a user attempts to store a non-numeric value ("abc") for that field on an employee record, the system rejects the input with a validation error. This validates FR-5 (type validation).

## 2. Related Requirements
- User Story: US-CHR-012
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "Badge Number" of type "Number" exists for Employee entity, marked as optional.
- An HR Officer is authenticated.
- An employee record exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field | Badge Number | Type: number |
| Invalid Value | abc | Non-numeric string |
| Valid Value | 12345 | Numeric value |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to an employee's profile, edit Custom Fields section. | The "Badge Number" field renders as a number input. |
| 2 | Enter "abc" into the "Badge Number" field. Attempt to save. | Client-side validation prevents submission with error message: "Badge Number must be a valid number" (or similar). |
| 3 | Attempt via API: `PATCH /api/v1/tenant/employees/{id}/profile` with `customFields: { badge_number: "abc" }`. | API returns HTTP 400/422 with validation error indicating type mismatch for the number field. |
| 4 | Enter "12345" into the "Badge Number" field. Save. | Success. The value "12345" is stored and displayed. |
| 5 | Verify via API: employee record shows `custom_fields: { "badge_number": 12345 }`. | The value is stored as a number in JSONB. |

## 6. Postconditions
- Invalid non-numeric values are rejected. Valid numeric values are stored correctly.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
