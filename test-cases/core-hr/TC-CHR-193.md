---
id: TC-CHR-193
user_story: US-CHR-008
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-193: Download document as owner employee via signed URL (happy path)

## 1. Test Objective
Verify that an employee authenticated in their own tenant context can download a document from their own record. The system generates a short-lived signed URL (5-minute expiry), the download succeeds, and the signed URL expires after 5 minutes. This validates AC-4 and FR-6.

## 2. Related Requirements
- User Story: US-CHR-008
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6, FR-10
- Non-Functional Requirements: NFR-6
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Employee "Jane Doe" (emp-001-uuid) exists in tenant "acme" and has a user account with Employee role.
- A document "employment-contract.pdf" (document_id = `doc-001-uuid`) exists on Jane Doe's record, uploaded by HR.
- Object storage is accessible.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User | Jane Doe | Employee role, owns emp-001-uuid |
| Document | employment-contract.pdf (doc-001-uuid) | Existing document on own record |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Jane Doe (Employee role) in tenant "acme". | JWT contains tenant_id for acme and employee_id matching emp-001-uuid. |
| 2 | Navigate to the Documents tab of Jane Doe's employee profile. | Document list loads showing "employment-contract.pdf" with download button (arrow-down icon). |
| 3 | Click the "Download" button on "employment-contract.pdf". | The system sends `GET /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-001-uuid}/download`. |
| 4 | Verify the API response. | Response status is 200. Response body contains a `signed_url` field with a pre-signed URL pointing to object storage, with an expiry parameter set to approximately 5 minutes from now. |
| 5 | Follow the signed URL. | The PDF file downloads successfully. Content-Type is `application/pdf`. File content matches the original upload. |
| 6 | Wait 6 minutes and attempt to use the same signed URL again. | The signed URL returns 403 Forbidden or equivalent expired-token error from object storage. |
| 7 | Verify an audit log entry was created for the download. | Audit log contains an entry with action "download", entity "employee_document", document_id = doc-001-uuid, user_id = Jane Doe's user ID. |

## 6. Postconditions
- The document is unchanged in storage.
- An audit log entry records the download event (NFR-6).
- The signed URL is no longer valid after 5 minutes.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
