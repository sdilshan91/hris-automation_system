---
name: reference-review-signoff-agreed-actions
description: US-PRF-006 agreed actions are an API-only surface — the Angular sign-off screen keeps them inside the notes body HTML, and never sends or renders the structured actions[] array
metadata:
  type: reference
---

`ReviewSignoffService.SaveNotesAsync` accepts a structured `Actions[]`
(`MeetingNotesActionInput(Description, Deadline)`) persisted to `review_meeting_notes_action`,
but the Angular `review-signoff.component.ts` does **not** use it: its meeting-notes template
writes an "Agreed development actions" `<h3>` section into the rich-text **body**, and the only
`el.innerHTML =` assignment (~:345) hydrates `meetingNotesHtml` — the Body field — not an action row.

**Why:** ISSUE-121 (agreed-action `Description` stored without `_sanitizer.Sanitize`) was filed as
elevated severity on the premise that the innerHTML sink at :345 renders the action text. It does not.
The defect was real and is fixed (sanitize before the blank-check, so a pure-payload action is skipped
rather than stored empty), but the exposure is API-only today: reachable via `POST` `SaveMeetingNotesRequest.Actions`
and returned in the notes + export-record DTOs.

**How to apply:** if a story asks to render agreed actions as a real list, that FE surface does not exist
yet — it is new work, not a binding. And when grading XSS severity here, check which field the innerHTML
sink actually receives; the Body/Strengths/DevelopmentAreas/Summary quartet has always been sanitized.
Related: [[reference-feedback360-config-authz]].
