---
name: design-review
description: Designer's-eye visual + UX audit of the running HRM UI in a real browser — first-impression, AI-slop detection, WCAG/typography/spacing/interaction checklist, and a goodwill/trunk test — producing a graded, screenshot-backed report. REPORT-ONLY (never edits code). Use to catch templated/"AI-generated" look, visual-hierarchy problems, and UX friction that /debug-ui and unit tests don't see.
user_invocable: true
---

# Design Review (HRM UI)

> Methodology adapted for this repo (MIT) from the gstack `design-review` skill (Garry Tan) and
> OpenAI's *"Designing Delightful Frontends"* slop taxonomy. Retargeted to Angular 20 + Angular
> Material + Tailwind, driven by our **Playwright MCP + Chrome DevTools MCP** (not gstack's `browse`
> CLI), and scoped to a **multi-tenant APP UI** rather than a marketing site.

Evaluates whether the rendered UI *feels right, looks intentional, and respects the user* — a
distinct axis from `/debug-ui` (console/network/DOM correctness) and `ng test` (behavior). It
answers "does this look designed, or does it look generated / templated / careless?" and grades it.

**REPORT-ONLY.** Like `/test-all` and `/debug-ui`, this skill **never edits code and never opens a
PR**. It evaluates the *rendered* site and writes a findings report. Fixes are a separate,
human-decided step (`@frontend-dev` / `/fix-finding`).

## Usage

```
/design-review                              # audit acme tenant on http://localhost:4200, default pages
/design-review http://localhost:4200/employees   # audit one route
/design-review --tenant acme --deep         # 10-15 pages, exhaustive checklist
/design-review --quick                       # login + 2 key pages, fast score
/design-review --diff                        # scope to routes touched by the current branch
```

Flags: `--quick` (3 pages), `--deep` (10-15 pages + every flow), `--diff` (branch-affected routes
only), `--tenant {sub}` (which tenant to sign in as; default `acme`). Default is a Full pass:
5-8 pages reachable after login.

## Prerequisites

1. **Browser MCP connected.** Playwright MCP + Chrome DevTools MCP are configured in `.mcp.json`.
   If just added, restart Claude Code and confirm with `/mcp`. If no `mcp__playwright__*` /
   `mcp__chrome-devtools__*` tools exist, stop and tell the user to reload.
2. **App running.** Frontend `http://localhost:4200` (`ng serve` in `src/frontend/`), backend per
   `src/frontend/src/environments/`. Pre-flight the URL (read-only) and abort with a clear message
   if down — don't assume it's up.
3. **A tenant + persona to sign in as.** Most of our UI is behind auth. Default `acme` / the QA
   personas (all `Admin@123!`, see the personas reseed note). State which tenant was active for
   every finding (Critical Rule #1).

## How it runs

Delegate the browser driving to the **`@browser-debugger`** sub-agent (it owns Playwright +
Chrome DevTools MCP and is read-only). Give it this skill's phases as its brief. It navigates,
screenshots, extracts the rendered design system via `browser_evaluate`, and runs
`lighthouse_audit` for perf/a11y. Keep the *report*, not the raw tool dumps. Screenshots save to
`.playwright-artifacts/` (gitignored); the graded report is written to
`docs/Design/design-reports/{tenant}-{scope}-{YYYY-MM-DD}.md`.

**Classifier first:** our HRM screens are **APP UI** (data-dense workspace: dashboards, employee
lists, admin console, settings). Apply the **App UI rules** below, not Landing-Page rules. The only
exception is the public login/marketing shell, if reviewed.

---

## UX principles (the lens — apply throughout)

- **Don't make me think.** Every screen self-evident. If a user pauses on "what do I click?" the
  design failed. Self-evident > self-explanatory > needs-explanation.
- **Users scan, they don't read.** Design for scanning: hierarchy = prominence, grouped areas,
  headings, highlighted key terms. Billboards at 60 mph, not brochures.
- **Users satisfice and muddle through.** They pick the first workable option and stick with it.
  Make the right choice the most visible choice.
- **Omit, then omit again.** Kill happy talk and instructions. If users must read instructions to
  operate a control, the control failed.
- **Goodwill reservoir.** Users start with goodwill; every friction point drains it (hidden info,
  format punishment, unnecessary fields, sloppy appearance). Replenish by making top tasks obvious,
  being upfront, saving steps, graceful error recovery.
- **Clarity trumps consistency.** If a small inconsistency makes something much clearer, choose
  clarity.

---

## Phase 1 — First impression (the designer output)

Form a gut reaction *before* analyzing. Full-page desktop screenshot, then narrate in **first
person**, naming specific elements by position and visual weight:

- "The screen communicates **[what]**." (competence? confusion? clutter?)
- "I notice **[specific observation]**."
- "The first 3 things my eye goes to are **[1]**, **[2]**, **[3]**." — are these what the designer
  intended? If not, the visual hierarchy is lying.
- "In one word: **[word]**."

> "My eye goes to the tenant logo top-left, then a wall of filter chips I skip, then… wait, is that
> a button or a badge?" — if you can't name the element specifically, you're generating platitudes,
> not scanning. Be opinionated; a designer reacts, they don't hedge.

**Area test:** point at each defined region — can you name its purpose in 2 seconds? ("employee
list", "filters", "bulk actions"). List any you can't.

## Phase 2 — Design system extraction (what's *rendered*, not what DESIGN docs claim)

Have `@browser-debugger` run these via `browser_evaluate` on each key page:

```js
// Fonts in use (cap 500 els)
[...new Set([...document.querySelectorAll('*')].slice(0,500).map(e => getComputedStyle(e).fontFamily))]
// Color palette in use
[...new Set([...document.querySelectorAll('*')].slice(0,500).flatMap(e => [getComputedStyle(e).color, getComputedStyle(e).backgroundColor]).filter(c => c!=='rgba(0, 0, 0, 0)'))]
// Heading hierarchy
[...document.querySelectorAll('h1,h2,h3,h4,h5,h6')].map(h => ({tag:h.tagName, text:h.textContent.trim().slice(0,50), size:getComputedStyle(h).fontSize, weight:getComputedStyle(h).fontWeight}))
// Undersized touch targets (<44px)
[...document.querySelectorAll('a,button,input,[role=button],mat-icon-button')].filter(e=>{const r=e.getBoundingClientRect();return r.width>0&&(r.width<44||r.height<44)}).map(e=>({tag:e.tagName,text:(e.textContent||'').trim().slice(0,30),w:Math.round(e.getBoundingClientRect().width),h:Math.round(e.getBoundingClientRect().height)})).slice(0,20)
```

Report as an **Inferred Design System**: Fonts (flag >3 families; flag if the primary is
`Inter/Roboto/Open Sans/Poppins/system-ui` → generic), Colors (flag >12 non-gray; warm/cool/mixed),
Heading scale (flag skipped levels, non-systematic jumps), Spacing (flag off-scale values). Note
where the rendered system diverges from Angular Material / Tailwind config expectations.

## Phase 3 — Page-by-page visual audit

For each page in scope: annotated + responsive screenshots (375 / 768 / 1024 / 1440), console
errors, and a Chrome DevTools `lighthouse_audit` (perf + a11y — a second a11y signal alongside axe).

### Trunk test (every page)
Dropped on this page with no context, can you answer: (1) what app is this? (2) what page am I on?
(3) what are the major sections? (4) what are my options here? (5) where am I (breadcrumb / active
nav)? (6) how do I search? Score **PASS** (all 6) / **PARTIAL** (4-5) / **FAIL** (≤3). A FAIL is a
HIGH finding regardless of polish.

### Checklist (10 categories — rate each finding high / medium / polish)

**1. Visual hierarchy** — one primary CTA per view; natural eye flow; no competing elements;
appropriate density; **squint test** (hierarchy still visible when blurred); white space
intentional; above-the-fold communicates purpose in 3s.

**2. Typography** — ≤3 fonts; scale follows a ratio (1.25/1.333); line-height ~1.5 body / 1.15-1.25
headings; measure 45-75 chars; no skipped heading levels; ≥2 weights for hierarchy; body ≥16px,
caption ≥12px; `text-wrap: balance` on headings; curly quotes + `…` not `...`; `tabular-nums` on
number columns (payroll/attendance grids); no letterspacing on lowercase.

**3. Color & contrast** — ≤12 non-gray colors; **WCAG AA** (body 4.5:1, large 3:1, UI 3:1);
semantic colors consistent (success green / error red / warning amber); no color-*only* encoding;
dark mode uses elevation not lightness-invert, text off-white not pure white, accent desaturated
10-20%, `color-scheme: dark` set; no red/green-only combos.

**4. Spacing & layout** — consistent grid at all breakpoints; 4/8px spacing scale, no arbitrary
values; nothing floats off-grid; related items closer, sections further; sane border-radius
hierarchy; **no horizontal scroll on mobile**; max content width set; URL reflects state (filters /
tabs / pagination in query params); flex/grid not JS measurement.

**5. Interaction states** — hover on all interactives; **`focus-visible` ring present** (never bare
`outline:none`); active/pressed depth; disabled = reduced opacity + `not-allowed`; loading skeletons
match real layout; **empty states** = message + primary action + visual (not "No items."); specific
error messages with a next step; success confirmation; **≥44px touch targets**; `cursor:pointer` on
clickables.

**6. Responsive** — mobile layout makes *design* sense (not just stacked desktop columns); ≥44px
targets; no horizontal scroll any viewport; responsive images; ≥16px body on mobile; nav collapses
sensibly; correct mobile input types; no `user-scalable=no`.

**7. Motion** — ease-out entering / ease-in exiting; 50-700ms; every animation communicates
something; **`prefers-reduced-motion` respected**; no `transition: all`; only `transform`/`opacity`
animated.

**8. Content & microcopy** — empty states with warmth; error = what + why + next step; specific
button labels ("Save changes" not "Submit"); no lorem/placeholder in prod; truncation handled;
active voice; **happy-talk detection** (kill "Welcome to…" / self-congratulatory blocks — report
"X words, Y% happy talk"); **instructions detection** (any visible instruction >1 sentence flags the
instruction AND the control it compensates for); destructive actions have confirm/undo.

**9. AI-slop detection (the blacklist — would a designer at a respected studio ship this?)**
1. Purple/violet/indigo gradient backgrounds or blue→purple schemes.
2. **The 3-column feature grid** (icon-in-colored-circle + bold title + 2-line blurb ×3) — the most
   recognizable AI layout.
3. Icons in colored circles as section decoration (SaaS-starter look).
4. Centered-everything (`text-align:center` on all headings/cards).
5. Uniform bubbly border-radius on every element.
6. Decorative blobs / floating circles / wavy SVG dividers.
7. Emoji as design elements (rockets in headings, emoji bullets).
8. Colored left-border on cards (`border-left: 3px solid <accent>`).
9. Generic hero copy ("Welcome to…", "Unlock the power of…", "Your all-in-one solution…").
10. Cookie-cutter section rhythm, every section same height.
11. `system-ui`/`-apple-system` as the **primary** display/body font — the "gave up on typography"
    signal.

**10. Performance as design** — LCP < 2.0s; CLS < 0.1; skeletons match layout; images lazy +
dimensioned + WebP/AVIF; `font-display: swap` + preconnect; no FOUT flash. Pull these from the
Chrome DevTools `lighthouse_audit` / `performance_*` trace.

## Phase 4 — Interaction flow review (feel, not just function)

Walk 2-3 key HRM flows (e.g. login → dashboard; employee list → profile → Documents tab; run
payroll → distribution). Snapshot, act, snapshot-diff. Evaluate response feel, transition quality,
feedback clarity, form polish (focus states, validation timing, errors near the source). Narrate in
first person.

### Goodwill reservoir (track across the flow; start 70/100, heuristic)
Subtract: hidden info user wants −15 · format punishment −10 · unnecessary fields −10 ·
interstitial/forced tour −15 · sloppy appearance −10 · ambiguous choice −5 each.
Add: top task obvious +10 · upfront about limits +5 · saves steps +5 each · graceful error recovery
+10 · apologizes on failure +5.
Report a step-by-step meter; <30 = critical UX debt, 30-60 = needs work, >60 = healthy. Surface the
biggest drains/fills as findings.

## Phase 5 — Cross-page consistency

Nav consistent across pages? Footer? Component reuse vs one-off (same button styled differently)?
Tone consistent? Spacing rhythm carried across pages? Since our UI is one Angular app with shared
Material components, one-off divergence usually means a component wasn't reused — flag it.

## Phase 6 — Compile the graded report

Write `docs/Design/design-reports/{tenant}-{scope}-{YYYY-MM-DD}.md` with:

- **Two headline grades:** **Design Score {A-F}** (weighted average) and **AI Slop Score {A-F}**
  (standalone, with a one-line verdict).
- **Per-category grades A-F:** A intentional/polished · B solid, minor issues · C functional but
  generic (no point of view) · D noticeable problems · F actively hurting UX. Each category starts
  at A; each **high** finding −1 letter, each **medium** −½, polish noted only.
- **Weights for Design Score:** Hierarchy 15 · Typography 15 · Spacing 15 · Color 10 · Interaction
  10 · Responsive 10 · Content 10 · AI-Slop 5 · Motion 5 · Perf 5.
- **Findings**, each: title · category · impact · tenant · page/route · screenshot path · specific
  "change X to Y because Z" recommendation (and the likely Angular component/SCSS to change, as a
  hand-off hint — but this skill does NOT edit it).
- **Quick Wins:** the 3-5 highest-impact fixes each < 30 min.
- **`design-baseline.json`** (score + per-category grades + findings) so a later `--diff`/regression
  run can show deltas.

## App UI rules (our default rule set)

- Calm surface hierarchy, strong typography, few colors; dense but readable; minimal chrome.
- Organize into: primary workspace · navigation · secondary context · one accent.
- **Avoid:** dashboard-card mosaics, thick borders, decorative gradients, ornamental icons.
- Cards only when the card *is* the interaction — no decorative card grids.
- Copy is utility language (orientation, status, action), not mood/brand/aspiration.
- Section headings state what the area is or what the user can do ("Selected KPIs", "Plan status").
- **Universal:** define CSS variables for color; no default font stacks; one job per section; "if
  deleting 30% of the copy improves it, keep deleting"; never body <16px or contrast <4.5:1; never
  placeholder-as-only-label; preserve visited-link distinction; never float a heading between
  paragraphs.

## Important rules

1. **Think like a designer, not a QA engineer** — you care whether it feels right and looks
   intentional, not only whether it "works."
2. **Screenshots are evidence** — every finding needs at least one, annotated where useful. **Read
   each screenshot file back** so the user sees it inline; without that, screenshots are invisible.
3. **Be specific and actionable** — "change X to Y because Z", never "the spacing feels off."
4. **Evaluate the rendered site, not the source** — don't grade the SCSS; grade what renders. (You
   may name the likely component as a fix hint for `@frontend-dev`.)
5. **AI-slop detection is the differentiator** — developers usually can't tell their UI looks
   generated. Be direct about it.
6. **Depth over breadth** — 5-10 well-documented findings with screenshots > 20 vague ones; write
   findings incrementally, don't batch.
7. **Multi-tenant:** state the active tenant for every finding; never print full JWTs/passwords
   (presence + claims only). Close the browser at the end.

## Relationship to our other skills

- **`/debug-ui`** = does it *work* (console/network/DOM). **`/design-review`** = does it look
  *designed*. Different axes; run both on a new/changed screen.
- Hand real defects (a render crash, a 500) discovered mid-audit to `/fault-diagnosis` /
  `@frontend-dev`; this skill logs the *design* findings and stops there (report-only).
- Findings worth turning into fixes flow into the human-decided cycle (`/fix-finding` or a dev
  agent), exactly like `TEST-FINDINGS.md` entries.
