---
id: TC-CHR-265
user_story: US-CHR-010
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-265: Cross-browser compatibility for bulk import page (Chrome, Edge, Firefox, Safari)

## 1. Test Objective
Verify that the bulk import page renders and functions correctly across Chrome, Edge, Firefox, and Safari. Template downloads, file upload, processing, results display, and error report download should work consistently.

## 2. Related Requirements
- User Story: US-CHR-010
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" exists and an HR Officer is authenticated.
- Latest stable versions of Chrome, Edge, Firefox, and Safari are available.
- A valid import file and a file with errors are prepared.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| Browsers | Chrome, Edge, Firefox, Safari | Latest stable |
| Valid File | cross_browser_test.csv | 5 valid rows |
| Error File | cross_browser_errors.csv | 3 valid, 2 invalid |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the import page in Chrome. Download CSV template, upload valid file, verify results. | All steps work. Summary shows "5 of 5 imported." Template file downloads. |
| 2 | Repeat in Edge. | Same behavior as Chrome. |
| 3 | Repeat in Firefox. | Same behavior. Drag-and-drop upload zone works. Error report downloads. |
| 4 | Repeat in Safari. | Same behavior. File picker works. Date fields in template parsed correctly. |
| 5 | In each browser, upload the error file and download the error report CSV. | Error report downloads correctly in all browsers with proper CSV content. |
| 6 | Verify visual consistency. | Layout, colors, animations (step transitions), and responsive behavior are consistent across all browsers. No rendering glitches. |

## 6. Postconditions
- Bulk import feature confirmed working across all target browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
