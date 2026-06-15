---
id: US-PLT-003
module: Platform
priority: Must Have
persona: Frontend Platform / All Users
status: draft
created: 2026-06-15
sprint: backlog
acceptance_criteria_count: 4
---

# US-PLT-003: Serialize API Enums as Strings + Reconcile Frontend Enum Casing

## 1. Description
**As a** frontend developer (and every end user),
**I want** the ASP.NET Core API to serialize enums as their string names (not integers) and the Angular enum value casing reconciled to match,
**So that** enum-typed fields (pipeline stage, application source, interview type/status, leave/attendance statuses, roles, etc.) bind correctly against a real backend instead of only against mocked unit tests.

## 2. Background / Problem Statement
The API has **no `JsonStringEnumConverter`** registered (verified: not in `Program.cs`, no `[JsonConverter]` on the enum types, no `AddJsonOptions`). System.Text.Json therefore serializes every enum as its **integer** value. The entire Angular frontend, however, consumes enums as **strings** — and inconsistently:
- PascalCase matching the C# member names: pipeline `stage` (`Applied`/`Screening`/…), interview `status` (`Scheduled`/`Completed`/`Cancelled`/`NoShow`).
- lowercase / kebab that do **not** match the C# names: application `source` (`public`/`internal`/`referral` vs C# `Public`/`Internal`/`Referral`), interview `type` (`in-person`/`video`/`phone` vs C# `InPerson`/`Video`/`Phone`).

Because both sides are only verified via mocked unit tests, the mismatch is invisible to the build/test gate but means enum fields fail to bind against the live API. Discovered while wiring US-REC-005 interviews; it is pre-existing and **cross-cutting across every module** (auth roles, leave, attendance, core-hr, recruitment). Same class of latent defect as [[US-PLT-001]]'s response-envelope mismatch.

## 3. Acceptance Criteria (IEEE 830 S3.2)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Any controller returns a DTO with an enum property | The response is serialized | The enum is emitted as its **string name**, not an integer, via a globally-registered `JsonStringEnumConverter` |
| AC-2 | The frontend sends an enum value in a request body / query | The API model-binds it | The string value binds to the enum (case-insensitive accepted) |
| AC-3 | A canonical casing is chosen (recommend: C# member names, PascalCase) | The frontend enum unions/labels are reconciled | Every FE enum value (esp. `ApplicationSource` and `InterviewType`) matches the wire value; affected specs updated to the canonical casing |
| AC-4 | The full suites run | `dotnet test`, `dotnet build`, `ng build`, `ng test` | All green; no test weakened or skipped |

## 4. Functional Requirements
- FR-1: Register `JsonStringEnumConverter` globally on the API (`AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))`), so requests AND responses use string enums.
- FR-2: Decide ONE canonical casing. **Recommended: the C# member names (PascalCase)** — least backend churn; keep `JsonStringEnumConverter` default (no naming policy) so names serialize verbatim.
- FR-3: Reconcile the Angular enum value unions, label maps, and badge maps to the canonical casing — notably `ApplicationSource` (`public`→`Public`, …) and `InterviewType` (`in-person`→`InPerson`, …). Stage/status are already PascalCase.
- FR-4: Update all affected FE specs (and any FE↔value comparisons / `Record<Enum,…>` maps) to the canonical values. This is alignment, not weakening.
- FR-5: Audit every module's DTOs for enum fields so none are missed (auth, core-hr, leave, attendance, recruitment, …).

## 5. Non-Functional Requirements
- NFR-1: No change to DTO/handler signatures or to backend unit tests (they assert on C# enum values, unaffected by JSON config).
- NFR-2: Accept incoming enum strings case-insensitively to be tolerant of older clients.

## 6. Out of Scope
- Changing enum members themselves; only the wire representation + FE casing.

## 7. Test Hints
- Add a backend test (WebApplicationFactory or a serialization unit test) asserting an enum DTO serializes to a quoted string, not a number.
- Grep the frontend for enum string literals (`'in-person'`, `'public'`, `'referral'`, etc.) and confirm each matches the canonical wire value after the change.
- Manually (or via the verify harness) hit a recruitment endpoint and confirm `stage`/`source`/`status`/`interviewType` are strings.

## 8. Notes
- Cross-cutting; do it as a dedicated story, not inside a feature. Pairs with [[US-PLT-001]] (envelope) as the second "never wired FE↔BE end-to-end" fix. Until this lands, recruitment (and other) enum fields are correct only in mocked tests.
