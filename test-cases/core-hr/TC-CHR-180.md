---
id: TC-CHR-180
user_story: US-CHR-007
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-180: Boundary -- field length limits enforced (name 150, postal 20, etc.)

## 1. Test Objective
Verify that the system enforces field length boundaries defined in the data schema: location name (varchar 150), address_line1/address_line2 (varchar 250 each), city (varchar 100), state_province (varchar 100), country (varchar 100), postal_code (varchar 20), time_zone (varchar 50), phone (varchar 20). Values at the exact limit should be accepted; values exceeding the limit should be rejected.

## 2. Related Requirements
- User Story: US-CHR-007
- Functional Requirements: FR-3, FR-4
- Data Requirements: Section 7 (Location table schema)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Max Length | At-Limit Value | Over-Limit Value | Notes |
|-------|-----------|----------------|------------------|-------|
| Location Name | 150 | "A" x 150 | "A" x 151 | Required |
| Address Line 1 | 250 | "B" x 250 | "B" x 251 | Optional |
| Address Line 2 | 250 | "C" x 250 | "C" x 251 | Optional |
| City | 100 | "D" x 100 | "D" x 101 | Optional |
| State/Province | 100 | "E" x 100 | "E" x 101 | Optional |
| Country | 100 | "F" x 100 | "F" x 101 | Optional (from list) |
| Postal Code | 20 | "12345678901234567890" | "123456789012345678901" | Optional |
| Time Zone | 50 | "America/Argentina/Buenos_Aires" (30 chars) | N/A (dropdown-constrained) | Required, IANA format |
| Phone | 20 | "+1234567890123456789" | "+12345678901234567890" | Optional |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the "Add Location" form | Form loads. |
| 2 | Enter a location name of exactly 150 characters, Time Zone = "Asia/Colombo" | Fields accept input at the boundary. |
| 3 | Fill all optional fields with values at their exact max length | All fields accept the boundary-length input. |
| 4 | Click "Save" | API returns 201 Created. All fields are stored with their full boundary-length values. |
| 5 | Verify all fields were persisted correctly at max length | Re-open the location edit form; all fields display their full boundary values without truncation. |
| 6 | Create another location: enter a name of 151 characters | Either client-side validation prevents input beyond 150 characters (input maxlength attribute), or the API returns a 422/400 validation error for exceeding the name length limit. |
| 7 | Test postal code of 21 characters | Either client-side prevents input beyond 20 characters, or the API returns a validation error. |
| 8 | Test phone of 21 characters | Either client-side prevents input beyond 20 characters, or the API returns a validation error. |

## 6. Postconditions
- One location exists with all fields at their maximum allowed lengths.
- No location exists with fields exceeding their maximum lengths.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
