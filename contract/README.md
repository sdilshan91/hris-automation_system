# Contract / property-based API testing (Schemathesis)

Fuzzes the **running** HRM API against its OpenAPI (Swagger) spec at
`/swagger/v1/swagger.json`. Schemathesis generates inputs from the schema and asserts the
responses conform — catching this platform's **recurring bug classes** automatically:

- **Unhandled `500`s** (`not_a_server_error`) — e.g. the Accrued-enum 500 class, DateTime-kind 500s.
- **FE↔BE response-shape drift** (`response_schema_conformance`) — the systemic Angular class
  where a paginated `{items,totalCount}` envelope is consumed as a bare array / `{data,total}`
  (BUG-099, BUG-236), and `ApiResponse<T>` envelope mismatches.

This is the spec-driven complement to `@test-runner`'s hand-written curl+JWT TCs — not a
replacement. It finds *classes* of contract bugs across all 457 operations without a human
enumerating cases. It is **report-only**: it never fixes code.

## Why not a REST-API MCP instead?
An OpenAPI→MCP that *calls* the API was considered and rejected for testing: it strips per-call
header control (needed for tenant-isolation probes), refuses schema-violating inputs (killing
negative/injection tests), and a write-capable config reintroduces the tenant/destructive blast
radius. Contract fuzzing from the spec is the higher-signal, lower-risk tool. See the Postgres MCP
section in `CLAUDE.md` for the parallel read-only stance.

## Safety
- **GET-only by default** (`METHODS=GET`) → read-only, safe against the live multi-tenant DB.
- Write methods require **explicit opt-in** and should only run against a throwaway DB
  (see `scratchpad/iso-fixture-seed.sql`), never shared dev data. Same discipline as the
  "no cross-tenant WRITE probes" rule.

## Prerequisites
- `uv` on PATH (`uvx` runs Schemathesis; no global install). Installed at `~/.local/bin`.
- API running on `http://localhost:5000` in **Development** (Swagger is dev-only). Native run
  needs DB creds + `Jwt:PrivateKey` in `dotnet user-secrets`.
- **Windows:** `PYTHONUTF8=1` is mandatory (the runner sets it) — Schemathesis emits box-drawing
  glyphs that crash under the cp1252 default when output is redirected.

## Run
```bash
bash contract/run-schemathesis.sh                 # GET-only, acme tenant
TENANT=techoneglobal bash contract/run-schemathesis.sh
TOKEN="eyJ..." bash contract/run-schemathesis.sh  # authed endpoints (else they 401)
MAX=5 WORKERS=6 bash contract/run-schemathesis.sh # more thorough
```
Reports are written to `contract/reports/` (gitignored).

## How `@test-runner` uses it
Run it like the k6 harness in `perf/`: a Bash-invoked, report-only capability. Feed failures into
`test-cases/TEST-FINDINGS.md` with the usual schema (type/severity/layer/root-cause/repro). A
Schemathesis failure ships a minimal reproduction (curl) — paste that as the repro step.

## Auth note
Schemathesis needs a JWT to exercise authed endpoints. Get one the same way the TCs do
(login to acme, persona `Admin@123!`) and pass it as `TOKEN=`. Without it, authed operations
return `401` — still useful for surfacing crashes on the unauthenticated surface.
