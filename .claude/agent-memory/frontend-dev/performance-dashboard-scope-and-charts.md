---
name: performance-dashboard-scope-and-charts
description: US-PRF-007 dashboard — scope (HR vs manager) is server-authoritative via overview.scope, NOT computed client-side; donut is pure SVG, trend is inline-SVG polyline (no chart.js)
metadata:
  type: feedback
---

US-PRF-007 Performance Dashboard: render HR-vs-manager scope off the backend's
authoritative `overview.scope` (`'Organization'|'Team'`), NOT from the FE role.
Org scope shows top/bottom performer lists; Team scope shows a single "team ranking"
(BR-3). The FE's only scope decision is the defensive redirect: a user lacking
`Performance.Read.All`/`Read.Team`/`Reports.View.All` is sent to `/my-review`
(employees see only their own data, AC-5) — checked via `auth.hasAnyPermission(...)`.

**Why:** like [[fe-iuser-no-employeeid]], ownership/scope gates can't be safely derived
client-side; the backend owns who-sees-what. Deciding scope in the FE would diverge from
RLS and risk showing org data to a manager.

**How to apply:** for any analytics/dashboard story with role-scoped data, add a single
`scope` (or equivalent flag) to the overview DTO and branch the template on it; keep the
FE's only client-side gate to the route redirect. Drive top/bottom vs team-ranking off
that flag, never off the JWT role.

Charting: continued option (b) — chart.js is STILL not a FE dependency despite the
story mandating it ([[no-chart-lib-comparison-table]]). Donut = two SVG `<circle>` arcs
(`donutGeometry()` returns dasharray+offset), histogram/dept bars = CSS/Tailwind widths,
multi-cycle trend = inline-SVG `<polyline>` (`trendPolylinePoints()` maps points to a
`0 0 W H` viewBox, skips null scores). All geometry is pure helpers in
`dashboard.models.ts` so specs assert math without a DOM (same as US-ATT-010/US-PAY-009).
