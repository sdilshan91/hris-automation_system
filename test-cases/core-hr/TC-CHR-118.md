---
id: TC-CHR-118
user_story: US-CHR-002
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-118: PII access recorded in audit log when viewing employee profile

## 1. Test Objective
Verify that viewing sensitive PII fields (viewing an employee profile) is recorded in the audit log, per NFR-4. The audit entry should record who accessed the data, when, and which employee's profile was viewed.

## 2. Related Requirements
- User Story: US-CHR-002
- Non-Functional Requirements: NFR-4
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer "Alice" is authenticated in "acme".
- Employee "Jane Doe" exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Alice |
| Employee ID | {jane_doe_id} | Target profile |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Note the current count of audit log entries for jane_doe_id | Baseline count = N. |
| 2 | Send `GET /api/v1/tenant/employees/{jane_doe_id}` as Alice (HR Officer) | Response is 200 OK with full profile data. |
| 3 | Query audit_log table for jane_doe_id | Count = N+1. A new entry exists with `action: employee_profile_viewed` (or equivalent), `user_id` matching Alice, `entity_id` matching jane_doe_id, `tenant_id` matching acme, and a timestamp. |
| 4 | Repeat: Employee "John Smith" views their own profile | A PII access audit entry is also created for self-service views. |

## 6. Postconditions
- Audit trail contains PII access records for accountability.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
