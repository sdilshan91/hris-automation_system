---
id: TC-CHR-329
user_story: US-CHR-013
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-07-15
---

# TC-CHR-329: WorkArrangement validation — an undefined enum value is rejected (AC-2 negative)

## 1. Test Objective
Verify US-CHR-013 AC-2 / FR-4 / BR-3 and spec §7.1: `Employee.WorkArrangement` accepts only the defined enum members (OnSite=0, Hybrid=1, Remote=2); an unknown/out-of-range value is rejected server-side, and the default is OnSite when omitted.

## 2. Related Requirements
- User Story: US-CHR-013
- Acceptance Criteria: AC-2
- Functional Requirement: FR-4
- Business Rule: BR-3
- Spec §7.1: only defined enum values

## 3. Preconditions
- An employee create/edit request context; actor with HR permission.

## 4. Test Data
| Field | Value | Expected |
|-------|-------|----------|
| WorkArrangement = 99 | undefined int | reject |
| WorkArrangement = "Offsite" | undefined name | reject |
| WorkArrangement omitted | — | default OnSite (0) |
| WorkArrangement = 2 (Remote) | valid | accept |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit `WorkArrangement = 99`. | 400 (undefined enum member); not persisted. |
| 2 | Submit an unknown string `"Offsite"`. | 400 (does not bind to a defined member). |
| 3 | Create an employee omitting `WorkArrangement`. | Persists default OnSite (0). |
| 4 | Submit `WorkArrangement = Remote`. | Accepted; persisted as Remote (2). |

## 6. Postconditions
- Only defined enum values persist; default is OnSite.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
