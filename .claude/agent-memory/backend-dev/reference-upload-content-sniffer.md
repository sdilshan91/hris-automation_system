---
name: reference-upload-content-sniffer
description: FileSignatureValidator seam for upload paths — fail-closed by design, the image/jpg alias, per-site adoption, and which upload path still has no virus scan
metadata:
  type: reference
---

`HRM.Application/Common/Security/FileSignatureValidator.cs` is the shared magic-byte sniffer
every upload path should call before it stores anything (BUG-058, then BUG-075 adoption).

**It is fail-CLOSED on unknown content types.** `Validate` rejects any `contentType` that is not a
key in `Signatures`. That is the reason adoption stalls on some sites rather than being a
one-liner: a site whose `AllowedContentTypes` contains a string the validator does not map
starts 400-rejecting *legitimate* files the moment you wire it in. Check the two lists agree
before adopting a new site.

**Widening the map is safe; narrowing is not.** Adding a key is purely additive — the key
previously matched nothing, so it was already rejecting everything. `image/jpg` (BUG-075) was
added this way as an alias of `image/jpeg`, sharing the same `Segment[][] Jpeg` field so the two
cannot drift. Proof that this is additive is cheap and worth doing: assert the alias and the
canonical type agree for *every* payload in both directions, and assert JPEG bytes are still
rejected for every other declared type.

**Not every allow-list uses the same strings.** `PayrollAdjustmentService` uses
`AllowedContentTypes`; most others use `AllowedMimeTypes`. Grep for both when surveying sites.

**Stream contract:** `ValidateStreamAsync` seeks to 0 on entry *and* exit, so it does not need an
existing post-virus-scan `stream.Position = 0` to piggyback on. A path with no scan step is fine.
The cheap way to prove the stream survived is an existing upload→download byte-equality
round-trip test — a missing rewind truncates the leading 16 sniff bytes and that assertion fails.
Mutation-verified 2026-09-07: deleting the exit `stream.Position = 0` turned 5 payroll arms RED,
including the pre-existing `SupportingDocument_UploadThenDownload_RoundTrips` (its 13-byte payload is
shorter than the 16-byte sniff window, so a non-rewound stream stores *nothing*).

**Known gap:** `PayrollAdjustmentService.UploadDocumentAsync` is the only upload path with **no
virus scan** (every other adopter calls `IVirusScanner.ScanAsync`). Filed separately by the user
on 2026-09-07; do not fix it inside a sniffer-adoption change.

See also [[feedback-mutation-check-revert-before-report]].
