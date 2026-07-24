---
id: TC-PLT-006
user_story: US-PLT-005
module: Platform
priority: high
type: functional
status: automated
created: 2026-07-24
automated: 2026-07-24
---

# TC-PLT-006: Encryption key-age watchdog — first-seen upsert + quarterly-cadence rotation-overdue WARN

## 1. Test Objective
Verify the `EncryptionKeyAgeWatchdogJob`. Because nothing in the key ring records WHEN a key was activated,
the watchdog upserts a **first-seen** timestamp per `Encryption:ActiveKeyId` into the system-scope
`encryption_key_activation` table and reports rotation overdue once the key's age reaches
`Encryption:RotationCadenceDays` (default 90). It proves: first run records first-seen at "now" (age 0, not
overdue); a later run does NOT advance first-seen (first-sight-only upsert) and ages correctly; the 90-day
boundary (89 = not overdue, 90 = overdue); the cadence config overrides the default; a key FLIP gets its own
first-seen row (age restarts) while the retired key's row is retained; and no configured active key returns
a null status. Driven over a real DI graph on an InMemory-through-real-EF store with the repo's
`FakeTimeProvider` clock seam.

## 2. Related Requirements
- User Story: US-PLT-005 — the operational side of AC-4 / the "KEK provisioning + rotation" NFR to author:
  the watchdog is what tells ops a key is due for rotation (rotation being what TC-PLT-004 performs).
- Related: TC-PLT-004 (the re-encrypt sweep the watchdog's WARN prompts ops to run).

## 3. Preconditions
- A DI graph with an `AppDbContext` (InMemory), a scoped `ITenantContext`, and `Encryption:ActiveKeyId`
  configured. The `FakeTimeProvider` supplies a controllable clock so "now" is deterministic. Unit-level; no
  container.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| T0 | 2026-07-01 08:00:00Z | fixed clock baseline |
| ActiveKeyId | hrm-field-key-1 | default |
| Default cadence | 90 days | quarterly |
| Override cadence | 30 days | via `Encryption:RotationCadenceDays` |
| Flipped key | hrm-field-key-2 | ops rotation (config change + restart) |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Run the watchdog at T0 (first sighting). | Status: keyId `hrm-field-key-1`, `AgeDays == 0`, `ThresholdDays == 90`, `RotationOverdue == false`; one activation row with `FirstSeenUtc == T0`. | `First_run_records_first_seen_now_and_is_not_overdue` |
| 2 | Run again at T0+10 days. | `AgeDays == 10`, not overdue; the activation row's `FirstSeenUtc` is STILL T0 (first-seen is written once). | `Later_run_does_not_advance_first_seen_and_ages_from_the_original_sighting` |
| 3 | Run at T0+89 and T0+90. | Day 89 not overdue; day 90 `AgeDays == 90` and `RotationOverdue == true` (90 IS the quarterly-cadence boundary). | `Day_89_is_not_overdue_but_day_90_is` |
| 4 | Set `RotationCadenceDays = 30`; run at T0+29 and T0+30. | Day 29 not overdue; day 30 `ThresholdDays == 30` and overdue — the config overrides the 90-day default. | `RotationCadenceDays_config_overrides_the_90_day_default` |
| 5 | After a T0 run under key-1, flip `ActiveKeyId` to key-2 (same DB) and run at T0+100. | Status keyId `hrm-field-key-2`, `AgeDays == 0` (new key's clock starts at ITS first sighting), not overdue; the table retains BOTH rows (key-1 then key-2). | `A_key_flip_restarts_the_age_clock_and_retains_the_retired_keys_row` |
| 6 | Run with NO configured active key. | Null status (test-host-only path; the real app fail-fasts at the encryptor). | `No_configured_active_key_returns_null_status` |

## 6. Postconditions
- The `encryption_key_activation` table records a first-seen row per active key; ops receives a
  rotation-overdue signal once a key reaches the configured cadence, without advancing an existing key's clock.

## 7. Test Category Tags
- [x] Happy path (first-seen record; ages correctly)
- [x] Negative test (no configured key → null status)
- [x] Boundary test (day-89 vs day-90 cadence boundary; config override)
- [ ] Security test
- [ ] Multi-tenant isolation (system-scope table — key activation is platform-wide, not tenant-scoped)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by:** `HRM.Tests/Unit/EncryptionKeyAgeWatchdogJobTests` (6 facts), carrying `[Trait("TC", "TC-PLT-006")]`. Runs in the agent verify gate (InMemory-through-real-EF + `FakeTimeProvider`).
