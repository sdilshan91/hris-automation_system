# Local Multi-Tenant HTTPS Dev Environment — Runbook

Run the HRM platform locally with **real subdomain multi-tenancy + TLS**, exactly like
production: browse `https://acme.myhrm.org` (clean URL, no port), the backend resolves the
tenant from the hostname, and logged-in sessions survive page reloads.

```
Browser ──HTTPS──▶ nginx :443 ──┬─ /api,/hangfire,/hubs ─▶ backend  127.0.0.1:5000
 https://acme.myhrm.org         └─ everything else       ─▶ ng serve 127.0.0.1:4200
        ▲ tenant = subdomain
```

> Branch: **`test/local-subdomains`** (this rig is committed here; it is **not** for merge to `main`).
> Switch to it first: `git checkout test/local-subdomains`

---

## A. Already done for you (no action needed)
These are committed on the branch / already on your machine:
- nginx reverse-proxy config → [`local-dev/nginx.dev.conf`](nginx.dev.conf)
- Portable nginx 1.30.3 → `local-dev/nginx/` (gitignored)
- Self-signed local CA + `*.myhrm.org` cert → `local-dev/certs/` (gitignored)
- Frontend wired for subdomains → `src/frontend/environment.ts` (relative `/api`, `baseDomain=myhrm.org`, tenant pin removed), `proxy.conf.json`, `angular.json` (`host=127.0.0.1`, `allowedHosts=['.myhrm.org']`)
- Backend `Platform:BaseDomain = myhrm.org` → set in **user-secrets**

---

## B. One-time setup — THINGS ONLY YOU CAN DO

### B1. 🔑 Hosts file (needs Administrator)
Open **`C:\Windows\System32\drivers\etc\hosts`** in Notepad **as Administrator** and add this **one line** (IP first — **no port, no `:4200`**):
```
127.0.0.1 myhrm.org acme.myhrm.org admin.myhrm.org techoneglobal.myhrm.org
```
Save, then flush DNS:
```powershell
ipconfig /flushdns
```
**Verify:** `ping acme.myhrm.org` must reply from `127.0.0.1`.

> Add more tenant subdomains to that same line as you create tenants (each must match a real
> tenant's `subdomain` in the DB, or it returns "workspace not found").

### B2. 🔒 Trust the local CA (removes the browser "not secure" warning)
Run in PowerShell (**CurrentUser store — no admin**; approve the popup), then **restart your browser**:
```powershell
Import-Certificate -FilePath "D:\WORK\hris-automation_system\local-dev\certs\ca.crt" -CertStoreLocation Cert:\CurrentUser\Root
```
Chrome & Edge use the Windows store. **Firefox** has its own — import `local-dev\certs\ca.crt` via
`Settings → Privacy & Security → Certificates → View Certificates → Authorities → Import` (tick "trust for websites").

### B3. 🗝️ Backend secrets (only if missing / on a fresh clone)
These live in **.NET user-secrets** (never in committed files). From `src/backend/HRM.Api`, make sure these three keys are set (use `dotnet user-secrets set "<key>" "<value>"`):
- **`ConnectionStrings:DefaultConnection`** — your local PostgreSQL connection. The format is in the committed `appsettings.json` template (`Host=localhost;Port=5432;Database=hris_dev_db;Username=developer;…`) — fill in your `developer` role's password.
- **`Jwt:PrivateKey`** — the JWT signing key.
- **`Platform:BaseDomain`** — set to `myhrm.org` (this is what makes subdomain resolution work).

Check what's already set: `dotnet user-secrets list`.

---

## C. Every time you want to run it (4 services)

Open 4 terminals (or run the first three in the background). **Order matters: Postgres → backend → frontend → nginx.**

### C1. PostgreSQL
Make sure your local PostgreSQL 18 service is running (DB `hris_dev_db`, user `developer`).
The backend auto-applies migrations + seeds on startup.

### C2. Backend API (`:5000`)
```bash
cd src/backend/HRM.Api
dotnet run
```
Wait for it to listen on `http://localhost:5000` (check: `curl http://localhost:5000/health` → 200).
**Run WITHOUT the VS Code debugger** (the debugger breaks on first-chance validation exceptions and looks like a hang).

### C3. Frontend (`:4200`)
```bash
cd src/frontend
npm start
```
Wait for "Compiled successfully". It binds to `127.0.0.1:4200`.

### C4. nginx (TLS proxy on `:443` / `:80`)
From the **repo root**:
```bash
local-dev/nginx/nginx.exe -p "d:/WORK/hris-automation_system/local-dev/nginx/" -c "d:/WORK/hris-automation_system/local-dev/nginx.dev.conf"
```
(no output = success). Verify: `curl -k https://acme.myhrm.org/api/v1/tenant/context` → 200 with `subdomain=acme`.

### C5. ✅ Browse
| URL | Tenant |
|---|---|
| **https://acme.myhrm.org** | **acme** — login `tenantadmin@acme.test` / `Admin@123!` (34 employees) |
| https://admin.myhrm.org | system / platform-admin context |
| https://techoneglobal.myhrm.org | techoneglobal tenant (needs the hosts entry from B1) |
| https://techone.myhrm.org | "workspace not found" (no such tenant — expected) |

Log in, press **F5** — you stay logged in (session persists over HTTPS).

---

## D. Stopping / reloading
```bash
# reload nginx after editing nginx.dev.conf
local-dev/nginx/nginx.exe -p "d:/WORK/hris-automation_system/local-dev/nginx/" -s reload
# stop nginx
local-dev/nginx/nginx.exe -p "d:/WORK/hris-automation_system/local-dev/nginx/" -s stop
```
Backend / frontend: Ctrl-C in their terminals (or kill the `dotnet` / `node` process).

---

## E. Regenerating the cert (only if it expires — valid ~2 years — or on a new machine)
```bash
cd local-dev/certs
export MSYS_NO_PATHCONV=1            # stop git-bash mangling the /CN= argument
printf '[req]\ndistinguished_name = req_dn\n[req_dn]\n' > openssl-min.cnf
printf 'subjectAltName=DNS:*.myhrm.org,DNS:myhrm.org,DNS:localhost,IP:127.0.0.1\nbasicConstraints=CA:FALSE\nkeyUsage=digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n' > ext.cnf
openssl req -x509 -newkey rsa:2048 -nodes -keyout ca.key -out ca.crt -days 825 -subj "/CN=HRM Local Dev CA" -config openssl-min.cnf
openssl req -newkey rsa:2048 -nodes -keyout myhrm.key -out myhrm.csr -subj "/CN=*.myhrm.org" -config openssl-min.cnf
openssl x509 -req -in myhrm.csr -CA ca.crt -CAkey ca.key -CAcreateserial -out myhrm.crt -days 825 -extfile ext.cnf
cat myhrm.crt ca.crt > myhrm.fullchain.crt
```
Then re-run **B2** (trust the new CA) and reload nginx.

---

## F. Troubleshooting (the exact issues we hit)
| Symptom | Cause → Fix |
|---|---|
| `ping acme.myhrm.org` → "could not find host" | Hosts line malformed. First token must be a bare IP `127.0.0.1` — **no `:4200`**, no `localhost:4200`. |
| Subdomain URL → connection refused (000) but `localhost:4200` works | ng serve bound to IPv6 `[::1]` only. Fixed via `angular.json` `host: 127.0.0.1`. Restart `npm start`. |
| Browser "Not secure" / cert warning | CA not trusted yet → do **B2**, restart browser. |
| Logged out after F5 (session lost) | You're on plain **HTTP** — browsers drop `Secure` cookies over HTTP. Use **https://** (via nginx). This is why TLS is required. |
| `https://...` fails entirely | nginx not running (**C4**) or backend/frontend down. Check `curl http://localhost:5000/health` and `curl http://localhost:4200`. |
| "workspace not found" on a subdomain | No tenant with that subdomain in the DB. Real tenants: `acme`, `techoneglobal`. |
| openssl "BIO_new_file: no such file" / subject mangled to `C:/Program Files/Git/...` | git-bash path conversion → prefix commands with `MSYS_NO_PATHCONV=1` and pass `-config openssl-min.cnf` (see **E**). |
| nginx won't start: "bind() to 0.0.0.0:443 failed" | Port 443/80 already taken (IIS, another nginx, Skype). Free it or change the `listen` ports in `nginx.dev.conf`. |

---

## G. Notes & related setups
- **What's faithful to prod here:** clean URLs (no port), `/api` path routing, real TLS, host-based tenant resolution (no dev `X-Tenant-Subdomain` header), persistent `Secure` sessions.
- **Don't merge this branch.** It changes `environment.ts`/`angular.json` for local subdomains; keep it as a local rig. The team's default dev flow (plain `localhost:4200` + `X-Tenant-Subdomain` header) still works on `main`.
- **Email/SMTP testing** is a **separate** feature on branch `feature/smtp-email-sender` (real MailKit `SmtpEmailSender`). To test email, switch to that branch and set `Smtp:Host`/`Port` (+ your Gmail app password already in user-secrets), or point at smtp4dev. It is not part of this subdomain rig.
- **Secrets:** the cert private keys (`local-dev/certs/*.key`) and nginx binary are gitignored — never commit them. The Gmail app password / DB password / JWT key live only in user-secrets.
</content>
</invoke>
