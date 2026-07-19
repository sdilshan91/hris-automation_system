---
id: TC-CHR-336
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: automated
created: 2026-07-20
defect:
  - DF-17
---

# TC-CHR-336: Org-tree consumes API-nested children inline on load, expands already-loaded branches with ZERO extra HTTP, and lazy-fetches only truncated nodes on expand — US-CHR-006 AC-1/AC-2 / FR-2/FR-6 (DF-17, FE-only)

## 1. Test Objective
Verify the DF-17 org-tree consumption contract on US-CHR-006 (frontend): the API returns each root node with its **direct children nested inline** down to the requested depth, and the Angular `OrgTreeService` + org-tree page consume that nested shape on load (children materialised into the tree without a per-node call). Expanding a node whose children were **already delivered** performs **zero additional HTTP** (FR-2 expand/collapse is a client-side operation on loaded data), while expanding a **truncated** deep node (children not yet delivered) issues exactly one lazy child-fetch (FR-6 lazy-load on expand). This is the **FE-only** counterpart to the backend org-tree endpoint; it is bound to the Angular Karma specs (no xUnit `[Trait]` — the binding is a `@TC-CHR-336` reference in the spec files).

## 2. Related Requirements
- User Story: US-CHR-006
- Acceptance Criteria: AC-1 (org chart renders the department hierarchy on load), AC-2 (clicking a node reveals its children / sub-departments)
- Functional Requirements: FR-2 (expand/collapse of tree nodes), FR-6 (lazy-load child nodes for deep hierarchies — API call on expand)
- Finding: DF-17 (API nests direct children inline per root down to the requested depth)

## 3. Preconditions
- The Angular org-tree feature under test: `OrgTreeService` (HTTP client for `/tenant/org-tree`) and `OrgTreePageComponent`, exercised via `TestBed` + `HttpTestingController` (Karma/Jasmine, headless Chrome).
- Fixture nodes shaped per DF-17: a root (e.g. "Engineering") carrying its `children[]` inline, plus at least one truncated deep node (`childrenCount > 0`, children absent).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Root node | Engineering (`childrenCount: 2`) with inline `children[]` | nested-on-load |
| Loaded child | Frontend (`childrenCount: 0`) | expand ⇒ no HTTP |
| Truncated node | deep node with `childrenCount > 0`, no `children` | expand ⇒ one lazy fetch |
| Base URL | `${apiBaseUrl}/tenant/org-tree` | tenant-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the tree; flush the root fetch carrying inline children. | The nested children are materialised into the tree model on load (consumed, not re-fetched). |
| 2 | Expand a node whose children were already delivered inline. | No additional HTTP request is issued (`httpMock.verify()` clean) — expand is a client-side toggle on loaded data. |
| 3 | Expand a truncated deep node (children not delivered). | Exactly one lazy child-fetch is issued and its response is merged under the node (fallback fetch). |

## 6. Postconditions
- The org tree renders from the nested payload with no redundant per-node calls; only genuinely-truncated branches trigger a lazy fetch on expand.

## 7. Test Category Tags
- [x] Happy path (nested children consumed on load)
- [x] Negative test (no redundant HTTP on already-loaded expand)
- [x] Boundary test (truncated node triggers exactly one fetch)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test (Karma headless Chrome; Angular Material tree)

## Automation & Traceability
- **FE-only binding (Angular Karma/Jasmine — no xUnit `[Trait]`).** The automated arms are the org-tree
  specs, each carrying a `@TC-CHR-336` reference comment above its top-level `describe`:
  - `src/frontend/src/app/features/core-hr/org-tree/services/org-tree.service.spec.ts` — nested-children
    consumption on load + lazy child-fetch on truncated-node expand.
  - `src/frontend/src/app/features/core-hr/org-tree/components/org-tree-page/org-tree-page.component.spec.ts` —
    page-level consumption, zero-HTTP expand on already-loaded branches, truncated-node fallback fetch.
- These specs pre-existed and are already green; this backfill only adds the `@TC-CHR-336` reference comment —
  no test was renamed, weakened, or restructured.
