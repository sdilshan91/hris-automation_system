---
id: TC-CHR-100
user_story: US-CHR-001
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-100: PII fields audit trail on access (NFR-6)

## 1. Test Objective
Verify that accessing PII fields (phone, emergency contact details) triggers entries in the audit trail, per NFR-6. Both read and write access to PII fields should be logged.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated.
- An employee "John Doe" exists with phone and emergency contact data populated.
- Audit logging is enabled and accessible.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Employee | John Doe | Has PII fields populated |
| PII fields | phone, emergency_contact_name, emergency_contact_phone | Fields to audit |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | View John Doe's employee profile (which includes phone and emergency contact) | Profile loads with PII fields visible. |
| 2 | Query the audit trail for the current user's access | An audit entry exists logging that the HR Officer accessed John Doe's PII fields (phone, emergency contact). |
| 3 | Update John Doe's phone number | Update succeeds. |
| 4 | Query the audit trail again | A new audit entry logs the PII field modification with old and new values (or indication of change). |
| 5 | Verify the audit entry includes: timestamp, user_id, action type (read/write), field names, tenant_id | All required metadata is present. |

## 6. Postconditions
- Audit trail contains entries for PII field access and modification.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
