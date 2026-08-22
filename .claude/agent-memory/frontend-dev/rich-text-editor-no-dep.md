---
name: rich-text-editor-no-dep
description: For "rich text editor" UI requirements, build a small in-repo contenteditable CVA instead of installing ngx-editor/TipTap, to keep the build+test gate lean
metadata:
  type: feedback
---

When a story asks for a rich text editor (e.g. US-REC-001 vacancy description/
qualifications), prefer a small in-repo `contenteditable` component implementing
ControlValueAccessor over installing a 3rd-party lib (ngx-editor, TipTap,
ngx-quill). See `features/recruitment/components/rich-text-editor`.

**Why:** the repo had NO RTE dependency installed. Adding ngx-editor pulls in
prosemirror and risks the unattended `/implement-all` verify gate (npm build +
ng test) over a foundation story. A `contenteditable` + `document.execCommand`
(bold/italic/underline/lists/createLink) editor is zero-dependency and the output
HTML is sanitized for free by Angular's default `[innerHTML]` binding (NFR-4 XSS)
— never bypassSecurityTrust.

**How to apply:** reuse this approach for any RTE field. execCommand is deprecated
but is still the only cross-browser zero-dep way to do inline formatting; jsdom/
Karma needs `spyOn(document,'execCommand')` and `spyOn(document,'queryCommandState')`
in specs. Treat `<br>`/`<div><br></div>` as empty so `required` validation behaves.
Only reach for ngx-editor/TipTap if a story truly needs tables/images/embeds.
