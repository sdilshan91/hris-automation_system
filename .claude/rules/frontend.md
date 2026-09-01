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
