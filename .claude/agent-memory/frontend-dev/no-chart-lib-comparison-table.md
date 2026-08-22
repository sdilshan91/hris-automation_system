---
name: no-chart-lib-comparison-table
description: No chart library is installed; draw charts in pure SVG/CSS (like the attendance dashboard) when they're core, or a table when they're a nicety
metadata:
  type: project
---

The frontend has NO chart library — chart.js, ngx-charts, and @swimlane are all
absent from package.json (only ngx-toastr + @ngx-translate). Do NOT vendor one.
Two established responses depending on how central the chart is:

1. **Chart is the core deliverable** (e.g. US-PAY-009 FR-5 analytics dashboard,
   US-ATT-010 attendance dashboard) → draw it in **pure SVG/CSS**:
   - LINE chart = an SVG `<polyline>` per series from pure `polylinePoints`/`lineX`/
     `lineY`/`trendMax`-style helpers kept in the feature's `*.models.ts` (unit-tested),
     mirroring attendance's `pointX`/`pointY` and the `buildDonutSegments` donut.
   - BAR (horizontal) = CSS bars with `[style.width.%]`, sorted desc.
   - STACKED bar = a `flex flex-col-reverse` column, each segment height = its frac of
     the column total.
   These are trivially unit-testable (assert the computed points/widths/segments) and
   add zero deps. US-PAY-009 reused this approach from US-ATT-010.

2. **Chart is a marked "nice-to-have"** (e.g. US-REC-006 FR-8 scorecard radar) → ship
   a clean **comparison table** and note the substitution. A radar/scatter is awkward in
   raw SVG for little payoff on an optional Phase-1 nicety.

**Why:** adding a chart dep bloats the build/test gate; the SVG/CSS approach already
covers line/bar/stacked/donut cleanly and is what the codebase standardized on.

**How to apply:** before reaching for a chart lib, grep package.json — it won't be
there. Default to SVG/CSS helpers in the models file (so they're unit-tested) for any
line/bar/donut/stacked chart. Only fall back to a table for radar-ish shapes on
explicitly-optional features. If a story truly needs a charting lib, flag the
build-budget/test cost to the caller first rather than vendoring silently.
