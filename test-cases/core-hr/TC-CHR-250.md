---
id: TC-CHR-250
user_story: US-CHR-010
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-250: Idempotency -- re-uploading the same file does not create duplicate employee records

## 1. Test Objective
Verify that re-uploading an identical import file does not create duplicate employee records. The email uniqueness check per tenant (FR-3, NFR-3) prevents duplicates; the second upload should report all rows as errors (duplicate emails already exist in the tenant). This validates NFR-3 idempotency.

## 2. Related Requirements
- User Story: US-CHR-010
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-3
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient capacity.
- An HR Officer user is authenticated in the "acme" tenant context.
- A previous import of `idempotent_test.csv` (5 rows) was already completed successfully; 5 employees now exist with the same emails.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | idempotent_test.csv | 5 rows, same emails as previously imported |
| Prior Import | 5 employees already exist with these emails | From first upload |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `idempotent_test.csv` (same file as previously imported) and click "Import". | System processes the file. |
| 2 | Wait for processing to complete. | Red summary banner: "0 of 5 records imported successfully. 5 records failed." |
| 3 | Verify the error table. | All 5 rows have errors: field `email`, error "Email already exists in this tenant" (or similar message indicating duplicate). |
| 4 | Query the `employees` table. | Employee count unchanged from before the second upload. No duplicate records. Only the original 5 exist. |
| 5 | Download the error report CSV. | Error report lists all 5 rows with duplicate email errors. |

## 6. Postconditions
- No new employees created on re-upload. Original 5 remain unchanged.
- System is idempotent with respect to email uniqueness.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
