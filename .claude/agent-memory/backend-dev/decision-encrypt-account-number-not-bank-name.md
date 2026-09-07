---
name: decision-encrypt-account-number-not-bank-name
description: ISSUE-523 — encrypt employees.bank_account_number only; bank_name and bank_branch_code stay plaintext deliberately, and a test pins that boundary.
metadata:
  type: project
---

`employees.bank_account_number` is encrypted at rest (ISSUE-523). `bank_name` and `bank_branch_code`
on the same entity are **deliberately not**, and `EncryptedFieldRegistryTests` has a test that goes red
if someone adds them.

**Why:** an IFSC / SWIFT-BIC branch code and a bank name are **public-directory** values that identify a
*branch*, not a person — neither is in the audit-log `SensitiveFieldMasker` deny-list that
`bank_account_number` is in, so the codebase's own PII classification already drew this line. Encrypting
them buys no confidentiality while permanently forfeiting SQL grouping/filtering by bank, which is how a
real bank-advice file is split per sponsor bank. The account number is the value that enables fraud.

The timing was also deliberate: it shipped **before** any bank-details capture path exists, so every
production row is structurally NULL and the back-fill is a provable no-op rather than a migration over
live PII. That window closes on the first successful write.

**How to apply:** if a future story wants to filter/group employees by bank, that is available today and
must stay so. If someone proposes encrypting the branch code, make them argue it — do not let it drift in.
See [[project-field-encryption-registry]] for the mechanics.
