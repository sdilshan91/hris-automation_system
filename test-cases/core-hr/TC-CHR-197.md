---
id: TC-CHR-197
user_story: US-CHR-008
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-197: Cross-tenant download attempt returns 403 and triggers security alert

## 1. Test Objective
Verify that a user authenticated in Tenant B who attempts to download a document belonging to Tenant A receives a 403 Forbidden response (not 404, since AC-4 explicitly specifies 403 for cross-tenant download attempts) and that a security alert is triggered. This validates AC-4 and NFR-3.

## 2. Related Requirements
- User Story: US-CHR-008
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6
- Non-Functional Requirements: NFR-3
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with a document "employment-contract.pdf" (doc-001-uuid) on employee emp-001-uuid.
- Tenant "globex" exists.
- An HR Officer user is authenticated in the "globex" tenant context.
- Security alerting / audit logging is active.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Owns the document |
| Tenant B | globex | Attacker's tenant context |
| Document ID | doc-001-uuid | Belongs to acme |
| Employee ID | emp-001-uuid | Belongs to acme |
| User Role | HR Officer (globex) | Authenticated in wrong tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "globex" tenant. | JWT contains tenant_id for globex. |
| 2 | Send `GET /api/v1/tenant/employees/{emp-001-uuid}/documents/{doc-001-uuid}/download` using the globex auth token. | Response status is 403 Forbidden. Response body contains an error message indicating cross-tenant access denied. No signed URL is returned. |
| 3 | Verify no file content was returned. | Response body does not contain any file data or signed URL. |
| 4 | Verify a security alert was triggered. | The audit log or security alert system contains an entry with: action = "cross_tenant_download_attempt", source_tenant = globex, target_document = doc-001-uuid, user_id = globex HR Officer's ID, severity = high or critical. |
| 5 | Verify the document in tenant "acme" is unaffected. | Authenticate as acme HR Officer, download the same document; download succeeds normally. |

## 6. Postconditions
- No file data or signed URL was leaked to the globex user.
- A security alert was logged for forensic review.
- The original document in acme remains intact and accessible to acme users.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
