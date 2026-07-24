---
type: runbook
date: 2026-07-24
status: current
tags: [local-dev, docker, rls, multi-tenancy, tls, subdomains, linux]
---

# Local dev on Linux + Docker (full stack · RLS flip · subdomain TLS)

Operational runbook for running the whole platform on a **Linux/Docker** box (verified on
Ubuntu 26.04 / OpenSSL 3.5 / Docker 29). The committed [`local-dev/README.md`](../../local-dev/README.md)
covers the **Windows** native-nginx rig; this note is the Linux/Docker counterpart. Related:
[[ADR-2026-07-10-tenant-isolation-model]].

## 1. Run the full stack
```bash
docker compose -f docker-compose.yml -f docker-compose.tls.yml --profile scanning up -d
```
- Services: `postgres17 · redis · backend (.NET10) · frontend (Angular/nginx) · clamav · nginx (TLS)`.
- `docker.env` (gitignored) holds all config/secrets. `POSTGRES_PASSWORD` **must** equal the `Password=`
  in `ConnectionStrings__DefaultConnection` — a one-char drift = backend `28P01` crash-loop.
- ClamAV is opt-in via the `scanning` profile; arm the app gate with `VirusScanning__ClamAv__Host=clamav`.
- App → http://localhost:4200 · API → http://localhost:5000/swagger · health → `/health`.

## 2. Flip RLS ON in the Docker DB (dev)
The mechanism is a startup reconciler gated on `Rls:Enabled` (not an EF migration). Steps that worked:
1. `roles.sql` (in `HRM.Infrastructure/Persistence/Rls/`) → creates `hrm_app` (NOBYPASSRLS) + `hrm_owner` (BYPASSRLS).
2. **Ownership:** a blanket `REASSIGN OWNED BY developer TO hrm_owner` **fails** — `developer` is the
   bootstrap **superuser** and owns system-pinned objects. Instead `ALTER … OWNER TO hrm_owner` each public
   table/sequence, and hand the `hangfire` schema to `hrm_owner` too (`DROP SCHEMA hangfire CASCADE` +
   `GRANT CREATE ON DATABASE … TO hrm_owner`; Hangfire recreates it) — else Hangfire 500s with `42501`.
3. In `docker.env`: `DefaultConnection`→`hrm_app`, add `PrivilegedConnection`→`hrm_owner`, `Rls__Enabled=true`.
   Hangfire auto-uses `PrivilegedConnection` when set (Program.cs).
4. Recreate the backend → reconciler `ENABLE+FORCE`s RLS on the tenant tables (~132).
- **Proof it enforces** (as `hrm_app`): no GUC → 0 rows · `SET app.current_tenant='<A>'` → only A's rows ·
  set B → A's rows hidden · `developer` (BYPASS) → all rows. Reversible via `Rls__Enabled=false` + restart.
- Gotcha: `developer` is a superuser → it **always bypasses RLS**, so testing on `developer` proves nothing.

## 3. Subdomain multi-tenancy over HTTPS (`*.myhrm.org`)
1. `./local-dev/gen-dev-certs.sh` → local CA (with `CA:TRUE` — **required**, OpenSSL 3.x rejects a CA without
   `basicConstraints`) + `*.myhrm.org` leaf → `local-dev/certs/` (gitignored).
2. Trust the CA: system (`sudo cp … /usr/local/share/ca-certificates/ && sudo update-ca-certificates`) **and**
   Chrome's own NSS store (`certutil -d sql:$HOME/.pki/nssdb -A -n "HRM Local Dev CA" -t "C,," -i …/ca.crt`;
   `certutil -N` first if the db is missing → `SEC_ERROR_BAD_DATABASE`).
3. `/etc/hosts`: `127.0.0.1 myhrm.org e2e.myhrm.org platform.myhrm.org admin.myhrm.org`.
4. Set `Platform__BaseDomain=myhrm.org` in `docker.env` (default is `yourhrm.com` → subdomains won't resolve).
- The `nginx` service (docker-compose.tls.yml) terminates TLS and proxies `/api`→backend, `/`→frontend.
- The FE on the `test/local-subdomains` branch is **already subdomain-mode** (`apiBaseUrl:'/api/v1'` relative,
  `baseDomain:'myhrm.org'`) — no rebuild; `https://e2e.myhrm.org` renders the "E2e" workspace, API calls are
  same-origin. Unknown subdomain → "This workspace does not exist".

## Misc gotchas hit this session
- The repo working tree is **CRLF** (old Windows checkout on a shared NTFS drive) → thousands of phantom
  "modified" files on Linux. Fix locally: `git config core.autocrlf input` (no file/history change).
- GitHub-MCP token: store in the GNOME keyring (`secret-tool`), export via `~/.bashrc`, and launch VS Code
  **from a terminal** so Claude Code inherits `${GITHUB_TOKEN}` (icon-launch won't have it).
