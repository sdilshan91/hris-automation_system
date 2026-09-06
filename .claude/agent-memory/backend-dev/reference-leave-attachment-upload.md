---
name: reference-leave-attachment-upload
description: ISSUE-036 leave attachment upload — upload-before-create with a NULLABLE LeaveRequestId claimed on create; the 4-part resolve gate; leave caps (5MB, PDF/JPG/PNG) deliberately differ from the self-assessment path
metadata:
  type: reference
---

`LeaveRequestAttachment` (ISSUE-036) is the platform's only **upload-BEFORE-the-parent-exists** attachment
entity. Design points another agent will otherwise get wrong:

- **`LeaveRequestId` is nullable by design.** The employee attaches the certificate while filling the leave
  form, so the row exists before the leave request. `LeaveRequestService.CreateAsync` *claims* it (sets the
  FK on the tracked row before the single `SaveChanges`). An unlinked row = pending upload.
- **The resolve gate is 4 conditions, not 1:** exists · tenant (global query filter) · `UploadedByEmployeeId
  == callerEmployee.Id` · `LeaveRequestId == null`. Any miss is a **400 `attachment_not_found`** — never
  404/403, so it never discloses that another employee's attachment exists. The already-linked condition is
  what stops one certificate covering two leave requests.
- **`AttachmentUrls` (text[]) is still populated** from the resolved rows' `StorageKey`s. Two read paths
  depend on it (`HasAttachments` on the pending queue, `Attachments` on `LeaveRequestDto`); dropping it
  silently blanks both.

**Caps deliberately differ from `SelfAssessmentAttachmentService` — do not "harmonize" them.**
Leave: **5 MB** and **PDF/JPEG/PNG only** (§10/NFR-3). Self-assessment: 10 MB and a much wider list including
Office types. The leave service also keeps an **extension** allow-list alongside the MIME one, because the
pre-fix validator checked extensions and dropping that would change the *kind* of validation
(`EmployeeDocumentService` does both too).

Related: [[reference-fresh-scope-rls-writes]], [[feedback-guards-must-be-mutation-proven]].
