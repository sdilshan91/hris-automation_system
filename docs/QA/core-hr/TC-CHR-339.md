---
id: TC-CHR-339
user_story: US-CHR-012
module: Core HR
priority: medium
type: functional
status: automated
created: 2026-07-21
automated: 2026-07-21
defect:
  - DF-9
---

# TC-CHR-339: Custom-field `options` wire-contract — the FE parses the backend's JSON-string options into an array (render) and stringifies on write (DF-9)

## 1. Test Objective
Verify the DF-9 fix to the custom-field `options` wire-contract drift. The backend serializes
`CustomFieldDefinitionDto.Options` and binds `Create/UpdateCustomFieldCommand.Options` as a **`string?`**
— a JSON-encoded string (e.g. `"[\"S\",\"M\",\"L\",\"XL\"]"`) — in **both** directions. The FE model
exposes `options` as `string[] | null` for ergonomic rendering (`@for (opt of cf.options)`). Before the
fix the FE never converted between the two, so a `dropdown`/`multi_select` field's options were iterated
as the **string's individual characters** (garbage choices in the employee wizard, the profile edit, and
the admin list), and the admin create/update POSTed a JSON array where the backend binds a `string?`
(400 / rejected). The fix normalizes at the single service boundary (`CustomFieldService`): parse
string→array on read, stringify array→string on write. Non-option field types keep `options: null`.

## 2. Related Requirements
- User Story: US-CHR-012 (tenant-configurable custom fields)
- Acceptance Criteria: dynamic custom fields render on the employee wizard/profile and round-trip their definitions
- Finding: DF-9 (custom-fields FE render pass — the `options` string↔array drift)

## 3. Preconditions
- `CustomFieldService` under `HttpTestingController` (unit); or the running stack with an active dropdown custom-field definition for the tenant (E2E).
- Backend `options` contract: a JSON-array string, both directions.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Wire options (dropdown) | `'["S","M","L","XL"]'` (string) | the raw backend shape |
| Parsed options (FE model) | `['S','M','L','XL']` (array) | what consumers must receive |
| Non-option field (text) | `options: null` | stays null |
| Malformed options string | `'not-json'` | degrades to null, no throw |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | `getActiveCustomFields('employee')` receives a definition whose wire `options` is the JSON string `'["S","M","L","XL"]'`. | The subscribed result's `options` is a real `string[]` equal to `['S','M','L','XL']` (`Array.isArray` true). | `custom-field.service.spec: DF-9: parses the backend options JSON STRING into a string[] array for rendering` |
| 2 | The grouped list path (`getCustomFields`) returns the same wire shape. | Definitions' `options` are parsed to arrays. | `custom-field.service.spec: getCustomFields ... options).toEqual(['S','M','L','XL'])` |
| 3 | A non-option (text) field arrives with `options: null`. | `options` stays `null`. | `custom-field.service.spec: DF-9: leaves a non-option field (options null) as null` |
| 4 | A malformed `options` string arrives. | `options` degrades to `null`; no throw. | `custom-field.service.spec: DF-9: a malformed options string degrades to null, never throws` |
| 5 | `createCustomField` with `options: ['S','M','L','XL']`. | The POST body's `options` is the JSON **string** `'["S","M","L","XL"]'` (matches the backend `string?` bind); the response string is parsed back to an array. | `custom-field.service.spec: DF-9: stringifies a dropdown field's options array into the backend string? contract, and parses the response back` |
| 6 | `updateCustomField` with an options array. | Same stringify-on-PUT + parse-on-response as create. | `custom-field.service.spec: DF-9: stringifies the options array on PUT and parses the response back` |
| 7 | **E2E (running stack):** open the Add-New-Employee wizard → Step 3 with an active "T-Shirt Size" dropdown. | The dropdown renders the four real options **S/M/L/XL** — not single characters. | `@browser-debugger` live verification (DF-9 session) |

## 6. Postconditions
- Dropdown/multi_select custom fields render their real option choices across the wizard, profile edit,
  and admin list; admin create/update round-trips option definitions against the backend `string?` contract.

## 7. Test Category Tags
- [x] Happy path (parse/stringify round-trip)
- [x] Negative test (malformed string → null, no throw)
- [x] Boundary test (non-option field → null)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in Karma):** the six DF-9 arms in
  `src/frontend/src/app/features/core-hr/custom-fields/services/custom-field.service.spec.ts` (parse-on-read,
  list-path parse, non-option null, malformed→null, create stringify+round-trip, update stringify+round-trip).
- **E2E:** `@browser-debugger` confirmed the wizard renders the "T-Shirt Size" dropdown's real S/M/L/XL options against the running stack (DF-9 session).
- Fix site: `CustomFieldService.parseOptions`/`serializeOptions`/`normalizeDefinition`/`toWirePayload`.
