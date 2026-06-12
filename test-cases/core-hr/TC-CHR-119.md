---
id: TC-CHR-119
user_story: US-CHR-002
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-119: Employee profile page load within 2.5 seconds P95 (NFR-1)

## 1. Test Objective
Verify that the employee profile page loads completely within 2.5 seconds at the 95th percentile on a simulated 4G connection, including all card sections rendering with data. This validates NFR-1.

## 2. Related Requirements
- User Story: US-CHR-002
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme" tenant.
- Employee "Jane Doe" exists with fully populated profile (all sections have data).
- Network conditions simulated at 4G (12 Mbps download, 1.4 Mbps upload, 70ms latency).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Employee ID | {jane_doe_id} | Fully populated profile |
| Network | 4G simulation | Chrome DevTools throttling |
| Iterations | 20 | Minimum for P95 calculation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Configure browser with 4G network throttling (or use a performance testing tool with equivalent constraints) | Network throttling active. |
| 2 | Navigate to `https://acme.yourhrm.com/employees/{jane_doe_id}` and measure time from navigation start to Largest Contentful Paint (LCP) | Record the load time. |
| 3 | Repeat step 2 for 20 iterations (clearing cache between each or using no-cache headers) | Record all 20 load times. |
| 4 | Calculate P95 from the 20 measurements | P95 value must be <= 2500ms. |
| 5 | Verify all card sections are fully rendered and interactive at the measured completion time | No skeleton placeholders remain after LCP. |

## 6. Postconditions
- Performance measurements recorded.
- P95 <= 2.5s confirmed or failed.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
