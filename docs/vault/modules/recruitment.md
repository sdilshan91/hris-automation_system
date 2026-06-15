---
type: module-note
module: recruitment
---

# Recruitment

Domain rules, edge cases, and decisions for the Recruitment module.

## Vacancy (US-REC-001)

The first Recruitment feature. Recruiter-facing internal app only — the anonymous
public careers page (FR-4/FR-5/NFR-5) is deferred to a later story.

### Frontend contract (camelCase DTOs, base `/api/v1/recruitment/vacancies`)
- `GET  /recruitment/vacancies?status=&departmentId=&search=&page=&pageSize=` → page envelope `{ data, total, page, pageSize }` (the FE also tolerates a bare array).
- `GET  /recruitment/vacancies/:id` → vacancy
- `POST /recruitment/vacancies` (body = create payload) → vacancy in `Draft`
- `PUT  /recruitment/vacancies/:id` (body = same payload) → updated vacancy
- `POST /recruitment/vacancies/:id/publish` → vacancy `Open` (backend validates BR-2 completeness)
- `POST /recruitment/vacancies/:id/close` → vacancy `Closed`
- `POST /recruitment/vacancies/:id/status` body `{ status }` → vacancy (backs the inline status dropdown)

Vacancy fields (camelCase): `id, referenceNumber, title, departmentId, jobTitleId,
employmentType, locationId, hiringManagerId, headcount, filledCount, salaryMin,
salaryMax, currency, description, qualifications, applicationDeadline, status, slug,
createdAt`. The FE create/update payload (`IVacancyRequest`) deliberately omits
`referenceNumber, slug, filledCount, status, tenantId` — those are server-managed.
The FE also expects display-name companions on reads (`departmentName`,
`jobTitleName`, `locationName`, `hiringManagerName`) so the list/table can render
without extra lookups.

### Business rules surfaced in the UI
- BR-2 publish-completeness: title + department + jobTitle + hiringManager +
  headcount(≥1) + description. FE validates this up front before calling `/publish`;
  the backend is still the authority. "Save as Draft" only requires `title`.
- Status enum + badge colors (§8): Draft=gray, Open=green, On Hold=amber,
  Closed=red, Cancelled=red.
- Salary range: max ≥ min (cross-field validator) when both set.

### Master-data dependencies
Form dropdowns reuse Core HR endpoints: `GET /departments`, `GET /job-titles`,
`GET /employees?search=` (hiring manager), and a `GET /locations` endpoint
(not yet confirmed in Core HR — backend should expose it or the FE location
dropdown stays empty, which is non-blocking since location is optional). All four
are normalized to a single `ILookupOption { id, label, sublabel? }` shape in
`VacancyService` so a DTO mismatch is a one-line fix.

### Rich text
Description + qualifications use a small in-repo `contenteditable` editor
(`RichTextEditorComponent`, a ControlValueAccessor) — NOT a 3rd-party lib — to
keep the build/test gate lean. Output is HTML displayed via Angular's default
`[innerHTML]` sanitizer (NFR-4); never bypassSecurityTrust. If a later story needs
tables/images in the JD, revisit (ngx-editor/TipTap).
