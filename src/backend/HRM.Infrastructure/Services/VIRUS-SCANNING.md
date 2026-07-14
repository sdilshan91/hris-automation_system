# Upload malware scanning — ClamAV deploy-gate (ISSUE-101)

Every file upload (applicant resumes, employee documents, onboarding assets/tasks, self-assessment evidence)
is scanned **before persistence**. The scanner is chosen by config at startup; this runbook is the operational
counterpart to the code — read it before enabling scanning in a real environment.

- **Abstraction:** [`../../HRM.Application/Common/Interfaces/IVirusScanner.cs`](../../HRM.Application/Common/Interfaces/IVirusScanner.cs)
  — `Task<VirusScanResult> ScanAsync(Stream, fileName, ct)`, returns `Clean()` / `Infected(threat)`, never throws-on-infected.
- **Real scanner:** [`ClamAvVirusScanner.cs`](ClamAvVirusScanner.cs) — INSTREAM over TCP using only the BCL socket
  stack (no NuGet ClamAV client, so **no GPL code is linked into this app**; clamd is a separate GPLv2 process
  reached over a socket). Protocol in [`ClamAvInStreamProtocol.cs`](ClamAvInStreamProtocol.cs).
- **Stub (default):** [`AllowWithLogVirusScanner.cs`](AllowWithLogVirusScanner.cs) — logs a Warning per call and
  passes the file through. Used in every environment until the gate below is armed.
- **DI gate:** [`../DependencyInjection.cs`](../DependencyInjection.cs) (`AddInfrastructure`) — mirrors the
  optional-Redis gate.

## The gate (env-var deploy-gate — mirrors the Encryption key gate)

```
VirusScanning:ClamAv:Host  ==  blank/absent   ->  AllowWithLogVirusScanner (pass-through, logged)
VirusScanning:ClamAv:Host  ==  set            ->  ClamAvVirusScanner (real scan)
```

`Host` is **blank in committed `appsettings.json`**, so local dev, the xUnit gate, and CI (none run a clamd) stay
green on the stub. Integration tests substitute `IVirusScanner` directly, so they are unaffected either way.

## Enabling in prod / staging

1. **Run a clamd** the app can reach over TCP (default port `3310`). Either:
   - the **`clamav` service in [`docker-compose.yml`](../../../../docker-compose.yml)** — it is behind the
     opt-in **`scanning` profile**, so it never starts in a normal local `docker compose up` (clamd needs
     **~1.5–2 GB RAM**). Bring it up in staging/prod with `docker compose --profile scanning up`; the `clamavdb`
     volume persists signatures across restarts, and first boot downloads the signature DB (~1–2 min), or
   - a `clamd` daemon on the app server, or a standalone `clamav/clamav` container.
2. **Set the host via env/secret BEFORE app start** (do NOT commit a host into `appsettings.json`) — for the
   compose stack, uncomment it in `docker.env`:
   ```
   VirusScanning__ClamAv__Host=clamav              # the compose service name (or a host/IP)
   VirusScanning__ClamAv__Port=3310                # optional (default 3310)
   ```
3. Restart the API. Confirm the log line shows the ClamAv scanner registered (the stub logs a "SKIPPED" warning
   on every scan — its absence means the real scanner is active). While clamd is still downloading signatures on
   first boot, the app fail-closes (uploads rejected) until it is healthy — expected; it self-heals.

## Fail behavior (money/safety-critical)

`FailOpen` defaults **false = fail-closed**: if clamd is down, unreachable, times out, or replies `ERROR`, the
scan returns `Infected("scan-unavailable")` and the upload is **rejected with HTTP 400** — a file is never stored
unscanned. This is why enabling the gate is a hard dependency on a running clamd. Set `VirusScanning__ClamAv__FailOpen=true`
only to deliberately allow-with-log when the scanner is unavailable (not recommended for a compliance posture).

Genuine request cancellation is re-thrown (never masked as a detection). Connect/scan timeouts are bounded by
`ConnectTimeoutSeconds` (10) / `ScanTimeoutSeconds` (60).

## Config keys (all under `VirusScanning:ClamAv`)

| Key | Default | Meaning |
|-----|---------|---------|
| `Host` | `""` | clamd host. Blank ⇒ stub scanner. Set via env in prod. |
| `Port` | `3310` | clamd TCP port. |
| `ConnectTimeoutSeconds` | `10` | TCP connect bound. |
| `ScanTimeoutSeconds` | `60` | whole-scan bound. |
| `FailOpen` | `false` | false = reject on scanner-unavailable (fail-closed). |

## Licensing note

ClamAV / clamd is free and open-source (**GPLv2**), usable commercially. Because this app invokes it as a
**separate process over a network socket** (no ClamAV code compiled or linked in), the HRM application is not a
derivative work and is not subject to GPL copyleft.
