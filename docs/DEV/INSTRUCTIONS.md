# DEV — INSTRUCTIONS

**Backend** (`src/backend`, .NET 10):
```
dotnet build HRM.sln
dotnet test HRM.sln            # Testcontainers → Docker must be running
dotnet run --project HRM.Api   # Swagger /swagger, Hangfire /hangfire (dev)
```
**Frontend** (`src/frontend`, Angular 20):
```
npm install && npm start       # ng serve
npm run build && npm test      # ng build / Karma
```
- **Canonical FE build/test path (ISSUE-326):** build via the Docker `frontend` service
  (`docker compose build frontend` / `-f docker-compose.yml`) **or** run `npm ci` on the host
  **before** building on Linux. **Never reuse a `node_modules` populated by a Windows `npm install`**
  on the shared NTFS drive — esbuild (and other native optional-deps) ships **platform-specific
  binaries**, so a win32 esbuild binary makes a host-Linux `npm run build` / `ng test` fail. Quick
  one-off recovery without a full reinstall: `npm install --no-save @esbuild/linux-x64`. Prefer the
  Docker service (clean, isolated) for anything reproducible.
- **Local stack / TLS subdomains:** [`../../local-dev/`](../../local-dev/) (nginx.dev.conf hardcodes `local-dev/certs/` — do not relocate).
- **Perf/load:** [`../../perf/`](../../perf/) (k6) — pairs with [`../QA/plans/INTEGRATION-PERF-TEST-PLAN.md`](../QA/plans/INTEGRATION-PERF-TEST-PLAN.md).
- **Ops:** [`../../ops/`](../../ops/).
