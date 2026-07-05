# Color Palette & Brand Style

**This is the single source of truth for all colors and brand-specific styles.** To customize diagrams for your own brand, edit this file — everything else in the skill is universal.

> **HRM SaaS brand.** Colors below are mapped to the HRM frontend theme
> (`src/frontend/tailwind.config.js` `brand`/`neutral` scales and
> `src/frontend/src/styles.scss` CSS variables + toast semantic colors), so
> diagrams match the product UI. Brand primary is azure **`#0c8ee9`** (`brand-500`);
> typography is **Inter**. Keep diagrams on the brand blue + neutral grays; use the
> green/red/amber/blue semantic pairs only to encode meaning (success/error/warning/info),
> exactly as the app does. Purple (`AI/LLM`) is the one intentional non-brand accent.

---

## Shape Colors (Semantic)

Colors encode meaning, not decoration. Each semantic purpose has a fill/stroke pair.

| Semantic Purpose | Fill | Stroke |
|------------------|------|--------|
| Primary/Neutral | `#e0effe` | `#0059a2` |
| Secondary | `#bae0fd` | `#054c85` |
| Tertiary | `#7cc8fc` | `#054c85` |
| Start/Trigger | `#fde68a` | `#92400e` |
| End/Success | `#bbf7d0` | `#166534` |
| Warning | `#fef3c7` | `#92400e` |
| Decision | `#fde68a` | `#b45309` |
| AI/LLM | `#ddd6fe` | `#6d28d9` |
| Inactive/Disabled | `#f5f5f5` | `#a3a3a3` (use dashed stroke) |
| Error | `#fecaca` | `#991b1b` |

**Rule**: Always pair a darker stroke with a lighter fill for contrast. Fills are `brand`/`neutral`
50–300 tints; strokes are 700–900 shades (or the app's semantic text color for success/error/warning/info).

---

## Text Colors (Hierarchy)

Use color on free-floating text to create visual hierarchy without containers.

| Level | Color | Use For |
|-------|-------|---------|
| Title | `#0059a2` | Section headings, major labels (`brand-700`) |
| Subtitle | `#0c8ee9` | Subheadings, secondary labels (`brand-500`) |
| Body/Detail | `#525252` | Descriptions, annotations, metadata (`neutral-600` = `--text-secondary`) |
| On light fills | `#171717` | Text inside light-colored shapes (`neutral-900` = `--text-primary`) |
| On dark fills | `#ffffff` | Text inside dark-colored shapes |

---

## Evidence Artifact Colors

Used for code snippets, data examples, and other concrete evidence inside technical diagrams.

| Artifact | Background | Text Color |
|----------|-----------|------------|
| Code snippet | `#171717` | Syntax-colored (language-appropriate) (`neutral-900`) |
| JSON/data example | `#171717` | `#22c55e` (green) |

---

## Default Stroke & Line Colors

| Element | Color |
|---------|-------|
| Arrows | Use the stroke color of the source element's semantic purpose |
| Structural lines (dividers, trees, timelines) | Brand stroke (`#0059a2`) or Neutral (`#737373`) |
| Marker dots (fill + stroke) | Brand primary fill (`#0c8ee9`) |

---

## Background

| Property | Value |
|----------|-------|
| Canvas background | `#ffffff` (`--surface-primary`) |
