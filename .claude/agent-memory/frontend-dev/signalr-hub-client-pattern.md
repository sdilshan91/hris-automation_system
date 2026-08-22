---
name: signalr-hub-client-pattern
description: SignalR (@microsoft/signalr) client service pattern — hub URL is at host root not /api/v1, JWT via accessTokenFactory, and how to fake the hub in specs
metadata:
  type: project
---

US-NTF-001 added the first `@microsoft/signalr` client (NotificationService,
`features/notifications/`). Reusable facts for any future realtime feature:

**Hub URL is at the HOST root, not under `/api/v1`.** `environment.apiBaseUrl` is
`http://localhost:5000/api/v1`; the hub lives at `/hubs/{name}`. Derive it by
stripping the `/api/...` suffix: `apiBaseUrl.replace(/\/api(\/.*)?$/, '') +
'/hubs/notifications'`. REST endpoints still use `apiBaseUrl` verbatim (bare
payload — apiEnvelopeInterceptor unwraps ApiResponse<T>).

**Auth:** `new HubConnectionBuilder().withUrl(hubUrl, { accessTokenFactory: () =>
authService.getAccessToken() ?? '' }).withAutomaticReconnect([0,2000,5000,10000,
30000])`. `AuthService.getAccessToken()` returns the in-memory JWT (it is NOT in
localStorage).

**Graceful degrade (NFR pattern):** if `hub.start()` rejects, fall back to
`timer(0, 30000)` polling of the REST list + unread-count, and switch on
`onclose`. Track a `connectionState` signal: connecting|connected|reconnecting|
polling|disconnected.

**Testing the hub (the tricky bit):** do NOT
`spyOn(signalR, 'HubConnectionBuilder')` — the ESM named import may be inlined by
esbuild and the spy won't intercept `new HubConnectionBuilder()`. Instead spy on
the PROTOTYPE: `spyOn(HubConnectionBuilder.prototype, 'build').and.returnValue(
fakeHub)` and `spyOn(...prototype, 'withUrl').and.callFake(function(){ return
this; })`. Read captured args off `HubConnectionBuilder.prototype.withUrl as
jasmine.Spy`. The fake hub records `.on('ReceiveNotification', cb)` handlers so a
test can simulate a server push by invoking the stored cb.

**Polling test gotcha:** see [[timer-polling-fakeasync-tick0]] — `timer(0,n)`
needs `tick(0)` before the first `expectOne`. Also: a poll cycle that calls BOTH
unread-count AND loadFirstPage will have the PAGE response's `unreadCount`
overwrite the count-endpoint value, so flush both with the SAME unreadCount or the
assertion sees the page's value (0).
