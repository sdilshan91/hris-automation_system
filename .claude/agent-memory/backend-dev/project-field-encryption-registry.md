---
name: project-field-encryption-registry
description: Encrypting a new column at rest touches five coupled places; the registry pin tests fail loudly if you miss one, but the design-time EF CLI needs an Encryption key env var or it will not scaffold.
metadata:
  type: project
---

Adding a field-at-rest encrypted column (AES-256-GCM `enc:v1:`) is a **five-part** change, and the
codebase enforces four of the five mechanically:

1. `EmployeeConfiguration.ApplyEncryption`-style static hook + the matching call in
   `AppDbContext.OnModelCreating` (a converter in the parameterless `Configure` alone stores PLAINTEXT).
2. `EncryptedFieldRegistry.Fields` entry — drives BOTH the startup back-fill and the key-rotation sweep.
3. `EncryptedFieldRegistryTests` pins the exact set in **two** tests (content pin + backfill count).
4. `dotnet ef migrations add` for the `varchar(n) → text` retype.
5. A raw-SQL ciphertext-on-disk assertion in `FieldEncryptionPostgresTests` — a round trip through the
   encrypting context proves nothing about what is on disk.

**Why:** the reverse-drift test `Every_encrypted_model_property_is_listed_in_the_registry` exists because
a converter without a registry entry is never re-encrypted on key rotation — retiring the old key would
**destroy** the column. Skipping step 2 is data loss, not a lint failure.

**How to apply:** step 4 needs the design-time env recipe in
[[reference-design-time-migration-env]] — `dotnet ef` boots the real host and fail-fasts with "Field
encryption is not configured" without `Encryption__ActiveKeyId` + `Encryption__Keys__{id}`. On 2026-09-07
(ISSUE-523) those **two** vars alone were enough; the PEM and connection string in that note were not
needed, so try the minimal pair first and add the rest only if the host still aborts.

Encrypting a column also **permanently forfeits SQL filtering, sorting, grouping and unique indexing** on
it (random-nonce ciphertext). Weigh that per column rather than encrypting a whole feature area — see
[[decision-encrypt-account-number-not-bank-name]].
