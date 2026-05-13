---
id: TC-AUTH-004
user_story: US-AUTH-001
module: Authentication
priority: high
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-004: Login form validation (empty fields)

## 1. Test Objective
Verify that the login form validates required fields and returns appropriate validation errors when email or password fields are submitted empty.

## 2. Related Requirements
- User Story: US-AUTH-001
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1
- Data Requirements: Input schema `{ email: string (max 150), password: string }`

## 3. Preconditions
- Tenant "acme" exists with status `active` and login page is accessible.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Email (Test 1) | (empty) | Missing required field |
| Password (Test 1) | (empty) | Missing required field |
| Email (Test 2) | john@acme.com | Valid email |
| Password (Test 2) | (empty) | Missing required field |
| Email (Test 3) | (empty) | Missing required field |
| Password (Test 3) | S3cure!Pass2026 | Valid password |
| Email (Test 4) | not-an-email | Invalid email format |
| Password (Test 4) | S3cure!Pass2026 | Valid password |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `https://acme.yourhrm.com/login` and click "Log in" with both fields empty | Client-side validation prevents submission; inline error messages appear for both fields ("Email is required", "Password is required"). |
| 2 | Send `POST /api/v1/auth/login` with `{ email: "", password: "" }` directly via API | HTTP 400 Bad Request with validation errors for both fields. |
| 3 | Enter a valid email but leave password empty; click "Log in" | Client-side validation shows "Password is required" inline error. |
| 4 | Send `POST /api/v1/auth/login` with `{ email: "john@acme.com", password: "" }` | HTTP 400 Bad Request with validation error for password field. |
| 5 | Leave email empty but enter a valid password; click "Log in" | Client-side validation shows "Email is required" inline error. |
| 6 | Send `POST /api/v1/auth/login` with `{ email: "", password: "S3cure!Pass2026" }` | HTTP 400 Bad Request with validation error for email field. |
| 7 | Enter an invalid email format `not-an-email` with a valid password; click "Log in" | Client-side validation shows "Please enter a valid email address" inline error. |
| 8 | Send `POST /api/v1/auth/login` with `{ email: "not-an-email", password: "S3cure!Pass2026" }` | HTTP 400 Bad Request with validation error for email format. |
| 9 | Verify that the "Log in" button is disabled while loading spinner is active | Button cannot be double-clicked during a pending request. |

## 6. Postconditions
- No tokens have been issued.
- No `failed_login_count` has been incremented (validation errors occur before credential check).
- Error messages are displayed inline below the form, not as browser alerts.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
