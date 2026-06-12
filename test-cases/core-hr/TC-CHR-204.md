---
id: TC-CHR-204
user_story: US-CHR-008
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-204: Expiry notification -- background job generates notifications at 30/7/1 day marks

## 1. Test Objective
Verify that the daily background job (scheduled at 09:00) correctly identifies documents expiring within 30, 7, and 1 days and sends notifications to the HR Officer and the employee. This validates AC-5, FR-8, and BR-4.

**DEFERRED NOTE:** The Notification module is not yet built. This test case documents the expected behavior for when the notification system is available. Until then, verify that the background job logic correctly identifies expiring documents and produces the correct notification payloads/records, even if actual delivery (email, in-app) is not yet functional.

## 2. Related Requirements
- User Story: US-CHR-008
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Employee "Jane Doe" (emp-001-uuid) has a document "visa-copy.pdf" with expiry_date = today + 7 days.
- Employee "Bob Smith" (emp-002-uuid) has a document "work-permit.pdf" with expiry_date = today + 30 days.
- Employee "Carol Lee" (emp-003-uuid) has a document "temp-pass.pdf" with expiry_date = today + 1 day.
- The daily document expiry check job is configured (Hangfire or equivalent).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Doc A | visa-copy.pdf (Jane) | expiry_date = today + 7 days |
| Doc B | work-permit.pdf (Bob) | expiry_date = today + 30 days |
| Doc C | temp-pass.pdf (Carol) | expiry_date = today + 1 day |
| Job Schedule | Daily at 09:00 | Hangfire recurring job |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Trigger the document expiry check background job manually (or wait for scheduled execution at 09:00). | Job executes successfully. |
| 2 | Verify the job identifies "visa-copy.pdf" as expiring within 7 days. | A notification record/payload is created for Jane Doe and the HR Officer with message indicating "visa-copy.pdf expires in 7 days". |
| 3 | Verify the job identifies "work-permit.pdf" as expiring within 30 days. | A notification record/payload is created for Bob Smith and the HR Officer with message indicating "work-permit.pdf expires in 30 days". |
| 4 | Verify the job identifies "temp-pass.pdf" as expiring within 1 day. | A notification record/payload is created for Carol Lee and the HR Officer with message indicating "temp-pass.pdf expires tomorrow". |
| 5 | Verify notifications are scoped to the correct tenant. | Each notification contains the correct tenant_id. No cross-tenant notifications are generated. |
| 6 | Run the job again on the same day. | Duplicate notifications are NOT sent (idempotency check). |
| 7 | **[DEFERRED]** Verify actual notification delivery (email/in-app). | When the Notification module is built: verify email sent and/or in-app notification visible in the notification center. |

## 6. Postconditions
- Notification records exist for all three expiring documents.
- Each notification targets both the employee and their HR Officer.
- No duplicate notifications for the same document on the same threshold day.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
