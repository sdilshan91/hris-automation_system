---
id: TC-LV-259
user_story: US-LV-008
module: Leave Management
priority: high
type: functional
status: automated
created: 2026-07-04
defect:
  - BUG-033
automated_by: HRM.Tests.Unit.GetCarryForwardPreviewQueryHandlerTests.CarryForwardPreview_OutOfRangeYear_Returns400Not500_BUG033
---

# TC-LV-259: Carry-forward preview with an out-of-range year returns a clean 400, not a 500 (BUG-033 regression)

## 1. Test Objective
Verify that `GetCarryForwardPreviewQuery` rejects an out-of-range `Year` (outside 2000..2100) with a clean **400** validation failure and never invokes the downstream carry-forward service — instead of letting an out-of-range year (e.g. 99999) reach the entitlement/date math and throw `ArgumentOutOfRangeException` as an HTTP **500**. Regression guard for **BUG-033**.

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-5 (read-only year-end preview)
- Functional Requirement: FR-5 (carry-forward / forfeiture preview for a closing year)
- Defect: BUG-033

## 3. Preconditions
- `GetCarryForwardPreviewQueryHandler` constructed with a mocked `ILeaveCarryForwardService`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Year (out-of-range) | 1999, 2101, 1800, 99999 | Must yield a clean 400; service never called |
| Year (valid) | 2025 | Positive control — previews normally |
| Year (null) | null | Positive control — defaults to current year (in range) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Handle the query with `Year = 99999` (and 1999 / 2101 / 1800). | Returns `IsFailure`, `StatusCode = 400`; no exception propagates (pre-fix: `ArgumentOutOfRangeException`/500). |
| 2 | Assert the service was not reached for the out-of-range year. | `PreviewYearEndAsync` received 0 calls — the only place the 500 could originate. |
| 3 | Handle the query with `Year = 2025`. | Returns `IsSuccess`; `PreviewYearEndAsync(2025)` received exactly once. |
| 4 | Handle the query with `Year = null`. | Returns `IsSuccess`; `PreviewYearEndAsync(currentYear)` received exactly once. |

## 6. Postconditions
- None (read-only query; commits nothing).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test (leave-year 2000..2100 bounds)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Automation
- **Harness:** xUnit + NSubstitute (InMemory/unit — pure handler input validation, no DB race or Postgres-specific behaviour). The mock throws `ArgumentOutOfRangeException` for out-of-range years, faithfully standing in for the real downstream 500, so the "Not500" claim is concrete; independent `DidNotReceive()`/`Received()` assertions verify the short-circuit two ways.
- **Binding:** `HRM.Tests.Unit.GetCarryForwardPreviewQueryHandlerTests` (`CarryForwardPreview_OutOfRangeYear_Returns400Not500_BUG033` [Theory] + `..._ValidYear_ReturnsPreview_BUG033` + `..._NullYear_UsesCurrentYear_BUG033`).
- **Pre-fix:** the handler forwarded the raw year with no guard → the service threw (500); the test fails. **Post-fix:** the 2000..2100 guard returns a clean 400 and never calls the service.
