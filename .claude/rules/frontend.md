---
paths:
  - "src/frontend/**"
---

# Frontend rules (Angular 20 · `src/frontend`)

## Commands
```bash
npm install
npm start            # ng serve — dev server
npm run build        # ng build
npm test             # ng test — Karma + Jasmine (single project, headful Chrome)
npm run lint         # ng lint — ESLint + angular-eslint (wired 2026-08-22)
npm run api:types    # regenerate api-types.ts from contracts/openapi/hrm-v1.json
npm run e2e          # playwright test
ng test --include='**/auth.service.spec.ts'   # run a single spec
```

## Working in a git worktree — read this before `ng test` fails at you

`node_modules/` is gitignored, so a **fresh worktree has no `src/frontend/node_modules`**. Every
frontend command then fails with `npm error could not determine executable to run` — there is no
global Angular CLI on this machine, so `ng` resolves *only* through `src/frontend/node_modules/.bin`.
The message names npx, not Angular, so it reads like a broken install rather than a missing tree.

**Do this:**

```bash
cd .claude/worktrees/<name>/src/frontend
npm ci                                   # ~1 min, 1145 packages
CHROME_BIN=/usr/bin/google-chrome npx ng test --watch=false --browsers=ChromeHeadlessNoSandbox
```

**Do NOT symlink the main checkout's `node_modules`.** It appears to work and is faster, and it is the
remedy one agent-memory note still recommends — but `docs/DEV/INSTRUCTIONS.md` forbids reusing a
`node_modules` populated by a Windows `npm install` on this shared NTFS drive, and `ISSUE-326` is the
incident: a win32 esbuild binary broke a host-Linux `ng test`. A symlink works only while the main
tree happens to be Linux-populated, which is not a property you can check at a glance.

It also **dirties the worktree**: `.gitignore`'s `node_modules/` has a trailing slash, which matches
directories only, and git does not treat a symlink as a directory. So the symlink shows up as
`?? src/frontend/node_modules` in `git status` and can end up in a commit.

## Architecture — standalone Angular 20

- `core/` holds singletons: `auth/` (service, guard, interceptor, models), `interceptors/`
  (`error`, `tenant`), `tenant/` (subdomain resolution mirroring the backend rules, using signals).
- `features/` holds route-lazy feature components (e.g. `auth/login`, `dashboard`);
  `layouts/` holds `auth-layout` / `main-layout`.
- HTTP interceptors are functional (`HttpInterceptorFn`). The `tenantInterceptor` injects
  `X-Tenant-Subdomain` from `environment.tenantSubdomain` for local dev.
- UI stack: Angular Material + Tailwind CSS, ngx-translate (i18n), ngx-toastr (notifications).

## Generated types are not source

`src/app/core/api/generated/api-types.ts` is emitted by `npm run api:types` from
`contracts/openapi/hrm-v1.json`, and CI compares it byte-for-byte (`npm run api:types:check`).

- **Never hand-edit it**, and never let `eslint --fix` touch it — it is excluded in `eslint.config.js`
  for exactly this reason (it accounted for 1,433 of the first run's 1,749 findings; auto-fixing them
  would have broken the contract gate).
- **FE/BE contract drift is this repo's dominant defect class.** ~660 hand-written `interface`s across
  ~77 `*.models.ts` files still need migrating to generated types — the decision is made, the work is
  not. Those files also hold **~443 `export type` declarations**, so size the work off ~1,100
  declarations, not 660: an earlier count that saw only `interface` undershot the surface by ~40%.
- **A blind `as` cast in a mapper is usually hiding the bug, not solving it** (BUG-127, BUG-311). If a
  mapper needs a cast to compile, check the wire shape before adding one.

## Accessibility

`npm run lint` currently reports **187 WCAG violations across ~121 templates** (ISSUE-389) —
`click-events-have-key-events` and `interactive-supports-focus` co-occur on the same element 60+ times
and are one fix. Static template linting and the runtime tools (`@axe-core/playwright`, Lighthouse,
`/design-review`) are complementary: runtime only sees components a test actually renders.
Do not add new violations.
