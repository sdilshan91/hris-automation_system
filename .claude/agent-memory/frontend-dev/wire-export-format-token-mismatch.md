---
name: wire-export-format-token-mismatch
description: Export-format tokens ship LOWERCASE (csv/xlsx/pdf) from ExportFormatNormalizer.Supported, but FE unions may be PascalCase — translate via a map, never cast
metadata:
  type: project
---

Every `availableExportFormats` field on the wire is filled from
`ExportFormatNormalizer.Supported` = `["csv", "xlsx", "pdf"]` — lowercase, and `xlsx`
(not `excel`). FE unions do not all agree: `recommendation.models.ts` uses lowercase
`'csv' | 'xlsx'`, but `dashboard.models.ts` `ExportFormat` is `'Csv' | 'Excel' | 'Pdf'`.
So for the dashboard the wire matches NO union member literally and needs a
translation map (`csv→Csv`, `xlsx→Excel`, `pdf→Pdf`), not a cast.

**Why:** `as ExportFormat[]` makes the union describe values the wire never sends —
that is literally BUG-311, and BUG-127 before it. Round-tripping the PascalCase value
back to the export endpoint is safe because the server's `Normalize` lowercases and
treats `excel` as an alias for `xlsx`.

**How to apply:** when mapping any `availableExportFormats`, map-then-filter so an
unrecognised token is DROPPED (no button whose handler the FE cannot honour) rather
than cast. See `mapExportFormats` / `isRecommendationExportFormat`. Related:
[[wire-migration-envelope-and-defaults]], [[stale-no-wire-source-comments]].
