---
type: agent-note
agent: frontend-dev
---

# @frontend-dev

Persistent notes for the frontend-dev agent.

Refer to the agent definition in [.claude/agents/team/frontend-dev.md](../../../.claude/agents/team/frontend-dev.md).

## UI conventions
*(component patterns, state management choices, design tokens — anything reused across modules)*

## Backend contract gotchas
*(quirks in the API contract the agent has learned the hard way — e.g. tenant header requirements, error shapes)*

- **Admin Console API roots differ.** US-ADM-002 monitoring lives at `/api/admin/monitoring`
  (the service strips the trailing `/v1` from `environment.apiBaseUrl`). But US-ADM-003
  impersonation lives at `/api/v1/system/impersonation` — i.e. UNDER `/v1/system`, so its
  service uses `environment.apiBaseUrl` verbatim + `/system/...`. Don't assume all System Admin
  endpoints share one root; check per story.
- **US-ADM-005/006 are TENANT-context, not system.** US-ADM-005 user-mgmt uses
  `apiBaseUrl` verbatim + `/users` (+ `/invitations`). US-ADM-006 company-settings
  uses `apiBaseUrl` verbatim + `/tenant/settings` (PUT sub-paths: `/org-profile`,
  `/localization`, `/password-policy`, `/session-policy`, `/primary-color`; POST
  multipart `/branding/upload`). Both carry `withCredentials` + the tenantInterceptor
  header; services consume bare payloads (NO ApiResponse unwrap), matching the rest
  of the FE. Nav "Settings" item → `/admin/settings` gated by `Admin.View`.

## i18n (ngx-translate)
- ngx-translate (`@ngx-translate/core` v16 + `@ngx-translate/http-loader`) is an installed
  dependency and `assets/i18n/en.json` exists, but it was **dormant** until US-ADM-003: nothing
  imported `TranslateModule` and no provider was registered. US-ADM-003 wired it up in
  `app.config.ts` via `provideTranslateService({ defaultLanguage: 'en', loader: TranslateHttpLoader })`
  and `app.component.ts` calls `translate.use('en')`. So i18n is now live app-wide; reuse it for
  new user-facing strings instead of hardcoding. v16 API: `provideTranslateService(config)` +
  `new TranslateHttpLoader(http, '/assets/i18n/', '.json')`; in specs use `provideTranslateService()`
  (fake loader returns the key, fine for presence assertions).

## Recurring tasks
*(setup steps repeated each new module — boilerplate, routing registration, etc.)*
