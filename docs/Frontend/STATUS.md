# Frontend — STATUS

- **Stack:** Angular 20 standalone + signals + OnPush · Angular Material + Tailwind · ngx-translate · ngx-toastr.
- **Multi-tenancy:** subdomain resolution mirrored on the FE (`core/tenant/`); dev uses `X-Tenant-Subdomain` header via `tenantInterceptor`.
- **Tests:** Karma + Jasmine (`npm test`). Open FE defects → [`BLOCKERS.md`](BLOCKERS.md).
