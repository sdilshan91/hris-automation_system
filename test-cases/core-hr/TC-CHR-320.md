---
id: TC-CHR-320
user_story: US-CHR-012
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-13
---

# TC-CHR-320: Custom field rendering on forms does not degrade page load by more than 200ms

## 1. Test Objective
Verify that rendering custom fields on employee creation and profile edit forms does not degrade page load time by more than 200ms compared to the baseline (no custom fields). This validates NFR-6.

**Type: Observational / Performance test.**

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-6

## 3. Preconditions
- Tenant "acme" exists with 20 custom fields defined (Professional plan maximum).
- Tenant Admin or HR Officer is authenticated.
- Baseline page load time has been measured with zero custom fields.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Baseline | Page load with 0 custom fields | Measured separately |
| Test | Page load with 20 custom fields | Max Professional plan |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Measure baseline: Navigate to employee creation form with 0 custom fields defined. Record DOMContentLoaded and full page load times (average of 10 loads). | Baseline recorded. |
| 2 | Define 20 custom fields of mixed types (text, dropdown, date, number, checkbox, etc.). | 20 fields created. |
| 3 | Navigate to employee creation form. Measure DOMContentLoaded and full page load times (average of 10 loads). | Page load time is at most 200ms slower than baseline. |
| 4 | Navigate to an existing employee's profile edit form. Measure similarly. | Page load degradation <= 200ms from baseline. |

## 6. Postconditions
- Performance measurements are recorded. Any degradation exceeding 200ms is flagged.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
