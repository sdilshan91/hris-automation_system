---
id: TC-AUTH-036
user_story: US-AUTH-005
module: Authentication
priority: medium
type: performance
status: draft
created: 2026-06-03
---

# TC-AUTH-036: MFA verification performance and rate limiting

## 1. Test Objective
Verify that the MFA verification endpoint meets the P95 latency target of 200ms or less, and that the rate limiter enforces a maximum of 5 attempts per session to prevent brute-force code guessing.

## 2. Related Requirements
- User Story: US-AUTH-005
- Non-Functional Requirements: NFR-1, NFR-4
- Functional Requirements: FR-2, FR-3

## 3. Preconditions
- The application is deployed in a performance-test environment representative of production.
- User pool: at least 50 test users with MFA enabled.
- Load testing tool (k6 or JMeter) is configured and connected to the test environment.
- Baseline system load is stable (no other performance tests running concurrently).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Target endpoint | POST /api/v1/auth/mfa/challenge | MFA verification |
| Fallback endpoint | POST /api/v1/auth/mfa/verify | Alternative if challenge is not separate |
| P95 latency target | <= 200 ms | NFR-1 |
| Rate limit | 5 attempts per session | NFR-4 |
| Load profile | 100 concurrent users, 60-second sustained | Simulates peak login traffic |
| Tool | k6 (preferred) or Apache JMeter | Load generation |

### k6 Test Script Outline
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  stages: [
    { duration: '15s', target: 50 },   // ramp up
    { duration: '60s', target: 100 },  // sustained load
    { duration: '15s', target: 0 },    // ramp down
  ],
  thresholds: {
    'http_req_duration{endpoint:mfa_verify}': ['p(95)<200'],
  },
};

export default function () {
  // Step 1: Login to get MFA challenge
  let loginRes = http.post(`${__ENV.BASE_URL}/api/v1/auth/login`, 
    JSON.stringify({ email: `user${__VU}@loadtest.com`, password: 'LoadTest!2026' }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  
  // Step 2: Submit MFA code
  let mfaRes = http.post(`${__ENV.BASE_URL}/api/v1/auth/mfa/challenge`,
    JSON.stringify({ code: generateTOTP(__VU) }),
    { headers: { 'Content-Type': 'application/json' }, tags: { endpoint: 'mfa_verify' } }
  );
  
  check(mfaRes, { 'MFA verify status 200': (r) => r.status === 200 });
  sleep(1);
}
```

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Deploy the k6 test script (or JMeter equivalent) targeting the MFA verification endpoint | Test harness is ready. |
| 2 | Execute the load test with 100 concurrent users over 60 seconds of sustained load | Test runs to completion without errors in the test framework. |
| 3 | Collect P95 response time for `POST /api/v1/auth/mfa/challenge` | P95 latency is <= 200 ms. |
| 4 | Collect P99 response time for the same endpoint | P99 is recorded for reference (no hard pass/fail, but should be <= 500 ms). |
| 5 | Verify error rate is below 1% | HTTP 5xx responses are < 1% of total requests. |
| 6 | **Rate limit test:** For a single session, submit 5 incorrect TOTP codes in rapid succession | Each returns HTTP 401 with "Invalid verification code." |
| 7 | Submit a 6th code in the same session | HTTP 429 Too Many Requests, or HTTP 401 with lockout message. The system refuses further attempts. |
| 8 | Verify the rate limit is per-session, not global | A different user/session can still submit MFA codes while the first session is rate-limited. |
| 9 | Wait for lockout/cooldown period and verify the session can retry | After the cooldown, MFA verification attempts are accepted again. |

### Pass Criteria
| Metric | Threshold | Hard/Soft |
|--------|-----------|-----------|
| P95 latency | <= 200 ms | Hard (fail if exceeded) |
| P99 latency | <= 500 ms | Soft (warning) |
| Error rate | < 1% | Hard |
| Rate limit enforcement | 5 attempts max per session | Hard |

## 6. Postconditions
- Performance test results are archived with timestamp.
- Any P95 violations are flagged as defects.
- Rate limiting is confirmed functional under load.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
