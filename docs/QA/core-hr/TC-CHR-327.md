---
id: TC-CHR-327
user_story: US-CHR-013
module: Core HR
priority: high
type: functional
status: draft
created: 2026-07-15
---

# TC-CHR-327: Employee.Fte validation — reject 0, negative, > 1.0, and > 2dp; accept 1.00 and 0.50 (AC-1 negative / boundary)

## 1. Test Objective
Verify US-CHR-013 AC-1 / FR-2 / BR-1 and spec §7.1: `Employee.Fte` is validated to `0 < Fte <= 1.0` with 2-decimal precision — `0`, negatives, `> 1.0`, and values with more than 2 decimals are rejected server-side (not silently clamped); `1.00` and `0.50` are accepted.

## 2. Related Requirements
- User Story: US-CHR-013
- Acceptance Criteria: AC-1
- Functional Requirement: FR-2
- Business Rule: BR-1
- Spec §7.1: `Employee.Fte` range + precision

## 3. Preconditions
- An employee create/edit request context; actor with HR permission.

## 4. Test Data
| Field | Value | Expected |
|-------|-------|----------|
| Fte = 0 | boundary-low | reject |
| Fte = -0.1 | negative | reject |
| Fte = 1.5 | over max | reject |
| Fte = 0.333 | > 2dp | reject (precision) |
| Fte = 1.00 | max | accept |
| Fte = 0.50 | valid | accept |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit `Fte = 0`. | 400 with a clear range message; not persisted. |
| 2 | Submit `Fte = -0.1`. | 400; not persisted. |
| 3 | Submit `Fte = 1.5`. | 400 (> 1.0); not persisted. |
| 4 | Submit `Fte = 0.333`. | 400 (precision > 2dp); not persisted. |
| 5 | Submit `Fte = 1.00`, then `Fte = 0.50`. | Both accepted (200/201); values persisted exactly. |

## 6. Postconditions
- Only in-range 2dp FTE values persist; invalid inputs rejected with a message.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
