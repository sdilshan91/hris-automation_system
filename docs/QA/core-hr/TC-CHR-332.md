---
id: TC-CHR-332
user_story: US-CHR-001
module: Core HR
priority: high
type: security
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - ISSUE-293
---

# TC-CHR-332: Employee National ID is encrypted PII — masked in DTOs by default, ciphertext at rest, full value only via an audited reveal (ISSUE-293)

## 1. Test Objective
Verify the ISSUE-293 fix: the employee **National ID** is treated as encrypted PII. It is **masked (last-4)** in every list/detail DTO, stored as **ciphertext** in the raw `national_id` column (`enc:v1:` prefix) and decrypts transparently on read, and the **full** value is returned only through an explicit `RevealNationalId` call that writes an `EmployeeNationalIdViewSensitive` audit record (field name only, never the value). A reveal for an unknown employee is a 404 that writes **no** audit.

## 2. Related Requirements
- User Story: US-CHR-001 (employee record / PII handling)
- Finding: ISSUE-293 (PR #371)
- Cross-cutting: PII-at-rest AES-256-GCM field encryption (see prior arc field-encryption infra)

## 3. Preconditions
- Unit path: `EmployeeService` over EF Core InMemory (mirrors `EmployeeServiceTests`) with a fake `IPayrollAuditLogger`.
- Persistence path: real PostgreSQL (Testcontainers) with the encrypting `AppDbContext` (mirrors `FieldEncryptionPostgresTests` / `FieldEncryptionIntegrationTests`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| National ID (input) | SL-931204567V | full plaintext supplied on create |
| Masked form | `MaskLast4` → `*...567V` | what DTOs carry |
| Raw column prefix | `enc:v1:` | ciphertext marker at rest |
| Audit action | EmployeeNationalIdViewSensitive | written on reveal only |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Create an employee with a National ID, then GET the DTO. | DTO's `NationalId` is the masked last-4 form, never the full value (ends `567V`, starts `*`). | `EmployeeServiceTests.Create_ThenGet_NationalId_IsMaskedInDto_ISSUE293` |
| 2 | Call `RevealNationalId` for that employee. | Returns the FULL decrypted value; an `EmployeeNationalIdViewSensitive` audit is written whose payload names the field but does NOT contain the actual value. | `EmployeeServiceTests.RevealNationalId_ReturnsFullValue_AndWritesViewSensitiveAudit_ISSUE293` |
| 3 | Call `RevealNationalId` for a non-existent employee id. | Failure with status **404**; NO reveal audit written (no access happened). | `EmployeeServiceTests.RevealNationalId_UnknownEmployee_Is404_ISSUE293` |
| 4 | Persist an employee on real Postgres; read the raw `national_id` column. | Raw column is ciphertext (`enc:v1:` prefix, no plaintext); the value decrypts back on read. | `FieldEncryptionPostgresTests.Persisted_values_are_ciphertext_in_the_raw_column_and_decrypt_on_read` |
| 5 | Round-trip an employee National ID through the encrypting context. | Written then read back equal (transparent encrypt/decrypt wiring proven). | `FieldEncryptionIntegrationTests.Employee_national_id_round_trips_through_the_encrypting_context` |

## 6. Postconditions
- National ID is never exposed in bulk DTOs, never stored in plaintext, and every full-value read is audited by field name only.

## 7. Test Category Tags
- [x] Happy path (masked default)
- [x] Negative test (404 no-audit)
- [ ] Boundary test
- [x] Security test (PII encryption + audited reveal)
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite):**
  - `EmployeeServiceTests.Create_ThenGet_NationalId_IsMaskedInDto_ISSUE293`
  - `EmployeeServiceTests.RevealNationalId_ReturnsFullValue_AndWritesViewSensitiveAudit_ISSUE293`
  - `EmployeeServiceTests.RevealNationalId_UnknownEmployee_Is404_ISSUE293`
  - `FieldEncryptionPostgresTests.Persisted_values_are_ciphertext_in_the_raw_column_and_decrypt_on_read` (national_id ciphertext arm — real Postgres/Testcontainers)
  - `FieldEncryptionIntegrationTests.Employee_national_id_round_trips_through_the_encrypting_context`
- Backing suite trait: `[Trait("TC", "TC-CHR-332")]`.
