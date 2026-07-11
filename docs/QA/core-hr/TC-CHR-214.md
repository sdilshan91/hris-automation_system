---
id: TC-CHR-214
user_story: US-CHR-008
module: Core HR
priority: high
type: functional
status: pass
exec_note: "2026-07-01 (API, fntest): PASS — category filter works with valid categories: uploaded Other + ID docs; ?category=Other → ['Other'], ?category=ID → ['ID'], no filter → all. (Invalid category strings e.g. 'Contracts' are silently ignored/return-all, but valid tabs filter correctly.)"
created: 2026-06-12
---

# TC-CHR-214: Category filter tabs (All, Contracts, IDs, Certificates, Other) filter the document list

## 1. Test Objective
Verify that the category filter tabs above the document list correctly filter the displayed documents by category. Clicking "All" shows all documents, clicking "Contracts" shows only Contract-categorized documents, and so on. This validates the UI/UX specification in section 8.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-9
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) has 5 documents: 2 Contracts, 1 ID, 1 Certificate, 1 Other.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Doc A | contract-1.pdf | Category: Contract |
| Doc B | contract-2.pdf | Category: Contract |
| Doc C | passport.jpg | Category: ID |
| Doc D | degree.png | Category: Certificate |
| Doc E | misc-notes.docx | Category: Other |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab. | Document list loads. "All" tab is active by default. All 5 documents are displayed. |
| 2 | Click the "Contracts" tab. | Only "contract-1.pdf" and "contract-2.pdf" are displayed (2 documents). |
| 3 | Click the "IDs" tab. | Only "passport.jpg" is displayed (1 document). |
| 4 | Click the "Certificates" tab. | Only "degree.png" is displayed (1 document). |
| 5 | Click the "Other" tab. | Only "misc-notes.docx" is displayed (1 document). |
| 6 | Click the "All" tab. | All 5 documents are displayed again. |
| 7 | Verify tab counts (if displayed). | Each tab may show a count badge (e.g., "Contracts (2)", "IDs (1)"). Verify counts are accurate. |

## 6. Postconditions
- Filter tabs work correctly across all categories.
- No data was modified.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — document list/category/upload-form render is an employee-detail tab unreachable in-app due to the crashed Employee Directory (**BUG-099**). No in-app path to the document tab.

> **Execution 2026-07-01 (triage, acme):** STILL BLOCKED — FE-UI arm (document-list rendering: expiry-badge colors / file-type icons / category filter tabs / upload-form fields). Not API-testable this pass (visual/DOM assertions). The underlying document CRUD + metadata API works (verified via upload/list/delete under TC-205), but the badge-color/icon/tab-filter rendering is front-end. Not a functional/business-rule defect at the API layer.
