# Frontend Template/Style Extraction + SCSS Standardization — TODO Plan

> **Status:** 📋 PLANNED — **not started.** Deferred deliberately: other sessions have in-flight
> dev work on live branches, so branch creation / `main` checkouts are unsafe right now. Pick this up
> when the working tree is clear.
>
> **Goal:** every Angular component uses **external** `templateUrl` + `styleUrl` (`.scss`) — no inline
> `template:` or `styles:` blocks — and all component styles are **SCSS**. This is a maintainability +
> consistency refactor (relocation of existing markup/styles), **not** a behavior change.
>
> **Owner:** driven by a new `/extract-templates` skill + the existing `frontend-dev` agent (see §4).

---

## Why (and the honest cost)

- Large components are hard to work in as one file — `main-layout` (1138 lines) and `applicant-detail`
  (1230 lines) mix TS + a 1000-line template + styles. External files restore IDE tooling (HTML
  language service, SCSS), cleaner diffs, and a single enforceable convention.
- **Cost is scale, not correctness.** ~300 extractions across 210 component decorators = large churn.
  Inline styles already use Tailwind `@apply` and compile through PostCSS, so extraction is
  *relocation only* (content moves verbatim) — low correctness risk. The real risk is **merge collisions
  with active branches**, which per-module batching (§5) contains.

---

## Current state (survey, 2026-07-14)

Root: `src/frontend/src/app` (Angular 20, standalone, OnPush).

| Metric | Count |
|---|---|
| `@Component` decorators total | **210** |
| Inline `template:` (backtick) | **179** |
| External `templateUrl:` | **25** |
| Inline `styles: [ … ]` | **134** |
| External `styleUrl` / `styleUrls` | **11** |
| Components with **no** styles | **63** |

- **SCSS is already the schematics default** (`angular.json` → `@schematics/angular:component.style = scss`);
  new components are fine. This plan retrofits existing ones.
- Inline styles are **SCSS/Tailwind-flavored** (`@apply …` + raw CSS) — safe to drop into `.scss` as-is.
- **7 existing `.css` component files** + **2 non-standard shared `*.styles.css`** files must become `.scss`.
- Only 2 `.scss` files exist today: global `src/styles.scss` and `login.component.scss`.
- **Specs are safe** — they import component classes, not template/style paths. No test changes needed
  for extraction (only additions if a module has zero coverage, which is out of scope here).

### The 11 existing external styles to normalize
- `.css → .scss` (rename + update `styleUrl`, content unchanged — plain CSS is valid SCSS):
  - `attendance/.../shift-management/shift-management.component.css`
  - `leave-management/.../holiday-calendar/holiday-calendar.component.css`
  - `leave-management/.../team-leave-calendar/team-leave-calendar.component.css`
  - `notifications/.../template-editor/template-editor.component.css`
  - `notifications/.../notification-preferences/notification-preferences.component.css`
- **Shared exception** — `admin/company-settings/company-settings.styles.css` (referenced by 4 section
  components via `../../`) + `.../branding-section/branding-section.styles.css`: rename to `.scss`,
  **keep shared** (documented exception; do not duplicate per-component). See DECISIONS.
- `login.component.ts` — already `.scss`; just normalize `styleUrls: ['…']` → `styleUrl: '…'`.

---

## Decisions (locked)

| # | Decision | Choice |
|---|----------|--------|
| D1 | Scope threshold | **Blanket** — extract *every* inline template and style block, even trivial ones (`:host{display:block}`, 20-line templates). One rule, no exceptions → easiest to enforce. |
| D2 | Rollout | **Per-module PR batches** (~13 PRs), each independently mergeable. Minimizes collision with live branches. |
| D3 | Tooling | **`/extract-templates` skill** driving the existing **`frontend-dev`** agent + a verify gate. No net-new agent. |
| D4 | Multi-`@Component` files | **Extract with suffixed names** — primary keeps `<name>.component.html/.scss`; each secondary (inline dialog/host) → `<name>-<selector-suffix>.component.html/.scss`. |

---

## §1 Target convention (end state)

- Template → `<name>.component.html`; styles → `<name>.component.scss` (**SCSS only**).
- Decorator uses **singular** `templateUrl` + `styleUrl` (Angular 20 idiom); normalize single-entry
  `styleUrls: ['…']` → `styleUrl: '…'`.
- **Multi-`@Component` `.ts`:** primary → `<name>.component.html/.scss`; each secondary →
  `<name>-<suffix>.component.html/.scss`, suffix derived from the secondary component's selector
  (e.g. `…-dialog`, `…-confirm`). The components stay in the same `.ts` file — only their template/style
  move out.
- Extraction is **relocation only** — inline content (incl. Tailwind `@apply`) moves **verbatim**; no
  rewrite, no reformat beyond what the move requires.

## §2 CSS→SCSS conversion
Handled per module as its components are processed (see the 11 files above). Plain CSS is valid SCSS, so
the rename is content-safe. The company-settings shared stylesheet is the one intentional shared-style
exception.

## §3 Success criteria
- **Per module:** 0 inline `template:` / `styles:` remaining in that module; `ng build` green;
  `ng test` green; PR opened.
- **Overall:** no `.css` component files remain; every decorator uses singular `templateUrl` + `styleUrl`;
  the four enforcement layers (§6) are in place.

## §4 The `/extract-templates [module]` skill

New driver skill; source of truth `.claude/skills/extract-templates.md`. Per invocation:

1. Read `docs/Frontend/TEMPLATE-EXTRACTION-STATUS.md`; pick the next `[ ]` module (or the arg-scoped one).
   Requires a **clean working tree on `main`**.
2. Enumerate that module's components with inline template/styles; cut
   `refactor/extract-templates-<module>` from fresh `main`.
3. Delegate to **`frontend-dev`** to extract **every** component in the module (blanket, §1 naming).
   The sub-agent does not commit.
4. **Verify gate:** `npm run build` → `npx ng test --watch=false --browsers=ChromeHeadlessNoSandbox`.
   Any failure → hand the **verbatim** errors back to `frontend-dev` (bounded retries). **Never** weaken,
   skip, or delete a test to go green (respects the `test-integrity-guard` hook).
5. On green: commit `refactor(<module>): extract inline templates/styles to external SCSS files`, push,
   open a PR, flip the module `[ ]`→`[x]` in the status ledger.

One module per call. Loop with `/loop /extract-templates`. Safe to run semi-attended: it's mechanical and
PRs are **opened, not auto-merged** — worst case is a stack of refactor PRs to review.

## §5 Rollout order — per-module checklist (~13 PRs)

> Validate the process on **one small module first**, then proceed. The two 1000+ line monsters
> (`layouts/main-layout`, `recruitment/applicant-detail`) get careful handling within their module PR.

- [ ] **Pilot:** one small/isolated area (e.g. a settings sub-area or `auth/forbidden`) — proves the skill + verify gate end-to-end
- [ ] `features/auth`
- [ ] `features/dashboard`
- [ ] `core-hr` / employees
- [ ] `features/leave-management` (incl. 2 `.css`→`.scss`)
- [ ] `features/attendance` (incl. `shift-management.css`→`.scss`)
- [ ] `features/recruitment` (⚠ `applicant-detail` 1230 lines)
- [ ] `features/payroll`
- [ ] `features/performance`
- [ ] `features/admin` (incl. company-settings shared-style exception)
- [ ] `features/notifications` (incl. 2 `.css`→`.scss`)
- [ ] `features/onboarding`
- [ ] `features/training`
- [ ] `features/reports`
- [ ] `layouts/` + `shared/` (⚠ `main-layout` 1138 lines)

_(Adjust module boundaries to the actual `src/frontend/src/app/features/*` folder list when starting.)_

## §6 Making the practice stick (the "update memory" part)

Four enforcement layers so this doesn't regress after the migration:

1. **`angular.json`** — set schematics explicitly: `inlineTemplate: false`, `inlineStyle: false`,
   `style: scss`, so future `ng generate` never produces inline/CSS components.
2. **`CLAUDE.md`** (frontend architecture section) — add the rule: *components use external `templateUrl`
   + `styleUrl` (`.scss`); no inline templates/styles.*
3. **`docs/Frontend/DECISIONS.md`** — record the convention + the company-settings shared-style exception.
4. **User auto-memory** — a `feedback`-type memory + `MEMORY.md` pointer so the practice is enforced in
   future agent sessions. _(Deferred with the rest until execution — see "When picking this up".)_

Optional stretch: an `angular-eslint` rule / CI check to fail the build on new inline templates/styles
(no first-class rule exists; would be a small custom check). Track under the tooling-adoption plan if wanted.

---

## When picking this up

1. Confirm the working tree is clean and no other session holds `main`.
2. Author `.claude/skills/extract-templates.md` (§4) and `docs/Frontend/TEMPLATE-EXTRACTION-STATUS.md`
   (the module checklist from §5 as the ledger).
3. Apply the §6 enforcement layers **first** (they're small, low-risk, and stop regression while the
   migration is in flight), including the user auto-memory `feedback` entry.
4. Run the pilot module, review its PR, then `/loop /extract-templates` through the rest.

_Plan authored 2026-07-14. Execution deferred at user request (other sessions active)._
