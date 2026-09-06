---
name: reference-design-time-migration-env
description: dotnet-ef migrations add needs 3 real config values via env; the Encryption key id must be hyphen-free or bash export rejects it, and Jwt__PrivateKey must be a real PEM
metadata:
  type: reference
---

`dotnet ef migrations add … --startup-project HRM.Api` **boots the real `Program.cs`**, so it fail-fasts on
the same blank-secret guards production does. Three values are needed, and two have traps:

```bash
cd src/backend
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$SCRATCH/dt.pem"
env \
  ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=x;Username=postgres;Password=postgres" \
  Jwt__PrivateKey="$(cat $SCRATCH/dt.pem)" \
  Encryption__ActiveKeyId="k1" \
  Encryption__Keys__k1="$(head -c 32 /dev/urandom | base64 -w0)" \
  dotnet ef migrations add <Name> --project HRM.Infrastructure --startup-project HRM.Api
```

**Why:** `JwtService` ctor calls `PemKeyHelpers.ImportPem` — a random base64 blob throws
`No supported key formats were found`. And the *documented* key id `hrm-field-key-1` contains hyphens, so
`export Encryption__Keys__hrm-field-key-1=…` fails with **"not a valid identifier"**; use a hyphen-free id
like `k1` and pass it via `env` rather than `export`. The connection string is never dialled — design time
only builds the model — so any well-formed value works.

**How to apply:** run this whenever `dotnet ef` dies with `HostAbortedException` / "Unable to create a
DbContext". Delete the temp PEM after. Then check the snapshot diff has **no `-` lines** — that is the
"no unrelated model drift" check.
