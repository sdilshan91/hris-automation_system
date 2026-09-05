#!/usr/bin/env bash
classify() {
  files="$1"
  non_docs=$(printf '%s\n' "$files" | grep -vE '^(docs/.+\.md|[^/]+\.md|\.claude/.+\.md)$' || true)
  [ -z "$non_docs" ] && { echo docs; return; }
  non_fe=$(printf '%s\n' "$files" | grep -vE '^src/frontend/' || true)
  [ -z "$non_fe" ] && { echo frontend; return; }
  non_be=$(printf '%s\n' "$files" | grep -vE '^src/backend/' || true)
  [ -z "$non_be" ] && { echo backend; return; }
  echo fullstack
}
t() { got=$(classify "$2"); [ "$got" = "$3" ] && echo "  PASS  $1 -> $got" || echo "  FAIL  $1 -> got '$got', want '$3'"; }

echo "== real PRs from this session =="
t "#627 docs-only"        "docs/QA/TEST-FINDINGS.md
docs/QA/plans/GAP-CLOSURE-QUEUE.md"                                     docs
t "#603 CLAUDE.md+skill"  "CLAUDE.md
.claude/skills/pr-pipeline.md"                                          docs
t "#628 frontend-only"    "src/frontend/src/app/core/auth/auth.guard.ts
src/frontend/src/app/layouts/main-layout/main-layout.component.ts"      frontend
t "#617 FE plan-override" "src/frontend/src/app/features/admin/plans/models/plan.models.ts"  frontend
t "ISSUE-117 backend"     "src/backend/HRM.Infrastructure/Services/InterviewService.cs
src/backend/HRM.Tests/Integration/InterviewSchedulingIntegrationTests.cs" backend
t "#624 BE + contract"    "src/backend/HRM.Infrastructure/Services/PlatformMonitoringService.cs
contracts/openapi/hrm-v1.json
src/frontend/src/app/core/api/generated/api-types.ts"                   fullstack
t "#615 scripts"          "scripts/ledger-lock.sh"                       fullstack
t "#606 nginx+compose"    "src/frontend/nginx.conf
docker-compose.yml"                                                     fullstack

echo "== traps =="
t "workflow itself"       ".github/workflows/ci-gate.yml"                fullstack
t "README at root"        "README.md"                                    docs
t "md inside src/"        "src/backend/NOTES.md"                         backend
t "docs non-md"           "docs/diagram.png"                             fullstack
t "mixed docs+code"       "docs/QA/TEST-FINDINGS.md
src/backend/X.cs"                                                       fullstack
t "mixed FE+BE"           "src/frontend/a.ts
src/backend/b.cs"                                                       fullstack
t "unknown top-level"     "ops/backup/run.sh"                            fullstack
t "gitattributes"         ".gitattributes"                               fullstack
