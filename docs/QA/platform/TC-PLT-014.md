---
id: TC-PLT-014
user_story: US-PLT-006
module: Platform
priority: medium
type: functional
status: draft
created: 2026-07-24
---

# TC-PLT-014: The GlitchTip Postgres volume (gt-pgdata) is enumerated by the backup/retention routine so error history survives a restore

## 1. Test Objective
Verify AC-7 (and NFR-5, FR-10). Self-hosting GlitchTip adds a Postgres data volume (`gt-pgdata`); its error
history must survive an infrastructure restore. This TC is an **ops/config check**: the backup/retention
routine must **include `gt-pgdata`** so a restore recovers the GlitchTip database along with the rest of the
platform's persistent volumes.

## 2. Related Requirements
- User Story: US-PLT-006
- Acceptance Criteria: AC-7
- Functional Requirement: FR-10 (add the GlitchTip Postgres volume to the backup/retention routine)
- Non-Functional: NFR-5 (operability; the Postgres volume is backed up)

## 3. Preconditions
- `ops/glitchtip/docker-compose.yml` exists and declares the `gt-pgdata` volume backing `gt-postgres`.
- A backup/retention routine (script or config) exists that enumerates the volumes/databases to back up.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Volume name | `gt-pgdata` | GlitchTip Postgres data volume |
| Compose file | `ops/glitchtip/docker-compose.yml` | declares the volume |
| Backup routine | ops backup script / retention config | must reference `gt-pgdata` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm `ops/glitchtip/docker-compose.yml` declares the `gt-pgdata` volume on `gt-postgres`. | The volume exists and is the GlitchTip DB store. |
| 2 | Inspect the backup/retention routine. | It enumerates `gt-pgdata` (or the `gt-postgres` DB) among the backed-up volumes/databases (FR-10). |
| 3 | (Ops verification) Perform a backup + restore drill of `gt-pgdata`. | GlitchTip error history (issues/events) is present after restore — history survived (AC-7). |

## 6. Postconditions
- GlitchTip error history is covered by backups; a restore recovers per-tenant issue history.

## 7. Test Category Tags
- [x] Happy path (volume enumerated in backups; restore recovers history)
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Intended binding:** an ops/config assertion tagged `@TC-PLT-014` (a CI grep or a backup-manifest test) that
  the backup routine references `gt-pgdata`; the restore drill is a manual ops step.
- **Status:** `draft` — the backup/retention routine referencing `gt-pgdata` is not yet in place (FR-10
  unimplemented). Flips to `automated`/`pass` when the routine and its check land. Do not mark `pass` without
  a real backup/restore verification.

## OUT-OF-LANE note
- Whether a platform-wide backup/retention routine that this volume must be *added to* actually exists is an
  **ops/infra** concern outside `docs/QA/`. Flagged in the report so the orchestrator can confirm the target
  routine exists before FR-10 is scheduled. This TC asserts the check regardless; the routine's existence is
  the dependency.
