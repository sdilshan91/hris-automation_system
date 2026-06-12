---
id: TC-CHR-214
user_story: US-CHR-008
module: Core HR
priority: high
type: functional
status: draft
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
