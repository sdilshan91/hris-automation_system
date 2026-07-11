# DEV — BLOCKERS

- No standing build blockers. Environment gotchas (stale `HRM.Api` DLL locks on rebuild → kill with PowerShell `Stop-Process -Force`; Testcontainers needs Docker up) are captured in [INSTRUCTIONS](INSTRUCTIONS.md) and the agent memory.
- Cross-cutting dev tasks surfaced by QA are tracked in [`../BA/STATUS.md`](../BA/STATUS.md) (QA-surfaced dev backlog).
