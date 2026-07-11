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
- **Local stack / TLS subdomains:** [`../../local-dev/`](../../local-dev/) (nginx.dev.conf hardcodes `local-dev/certs/` — do not relocate).
- **Perf/load:** [`../../perf/`](../../perf/) (k6) — pairs with [`../QA/plans/INTEGRATION-PERF-TEST-PLAN.md`](../QA/plans/INTEGRATION-PERF-TEST-PLAN.md).
- **Ops:** [`../../ops/`](../../ops/).
