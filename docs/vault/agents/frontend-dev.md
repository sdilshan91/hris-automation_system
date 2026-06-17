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

- **US-NTF-002 notification-templates is TENANT context.** Email-template
  endpoints are `apiBaseUrl` verbatim + `/notification-templates` (GET list,
  GET/PUT/DELETE `/{eventKey}`, POST `/{eventKey}/preview`, POST
  `/{eventKey}/test-email`) — same root style as US-NTF-001. Routed at
  `/admin/notification-templates` gated `roleGuard(['Tenant Admin','Tenant Owner'])`.
  The rich text editor REUSES the US-REC-001
  `recruitment/components/rich-text-editor` (dependency-free contenteditable CVA);
  US-NTF-002 added a public `insertText(text)` to it for the variable-panel
  placeholder insertion. No ngx-quill/TipTap added (kept the build+test gate lean).

- **US-NTF-003 notification-preferences is PERSONAL tenant context.** Endpoints
  are `apiBaseUrl` verbatim + `/notification-preferences` (GET matrix, PUT
  `/{category}` body `{channelInApp,channelEmail}`, PUT `/quiet-hours`, POST
  `/reset`) — same root style as US-NTF-001/002. Routed at
  `/profile/notification-preferences` with NO roleGuard (personal settings; parent
  authGuard suffices, backend scopes to identity + tenant membership BR-4). The
  per-category PUT may 400/422 ("at least one channel" BR-3 / mandatory AC-3) — the
  matrix component toggles OPTIMISTICALLY then reverts to the pre-toggle snapshot on
  error. Auto-save is debounced 500ms via a `Subject` +
  `takeUntilDestroyed(this.destroyRef)` (NOT bare `takeUntilDestroyed()` — that
  throws NG0203 when called inside ngOnInit, only works in an injection context).
  NOTE: the global errorInterceptor already toasts 422 messages, so the component
  only re-toasts for 400/422 the server's own `error.message` and lets other
  statuses fall through to the interceptor.

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
