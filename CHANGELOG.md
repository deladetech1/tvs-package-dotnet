# Changelog

## 1.0.5 (2026-10-01)

Tenant routing, mirroring the Python package's `trovesuite.tenancy`. Nothing in this
release changes how a request is answered unless it is switched on.

- `Tenancy/TenantRoute` — one row of `control_plane.ctl_tenant_routes`. Carries secret
  URIs, never credentials.
- `Tenancy/TenantContext` — which database this request belongs to, in an `AsyncLocal`.
  `Current` may be null; `Require()` throws. There is deliberately no accessor that
  defaults to the shared database. `Scope()` is how background work declares a tenant;
  `TenantlessRead()` and `SpansAllTenants(reason)` are the two markers — the first says
  "no tenant, ever", the second says "correct today, must become a per-tenant loop".
- `Tenancy/TenantRouteResolver` — host to route, cached per process, misses cached too.
  A missing or unreadable table is a warning once, not a 500 per request, because the
  package can reach a deployment before the migration does.
- `Tenancy/TenantContextMiddleware` — resolves from `Origin`, then `X-Tenant-Host`, then
  `Host`; first that RESOLVES wins. The API is reached at its own hostname, so `Host`
  names the app and almost never the tenant. Register it INSIDE `UseCors`.
- `UnscopedAccessReporter` — reports, once per call site, that the database was reached
  with no tenant. Public, because a consumer's own connection pool or EF interceptor has
  to call it too, or the list comes back empty and looks like finished work.
- `TenancyOptions` under `Trovesuite:Tenancy`. `Enforce` and `TrustForwardedHost` both
  default off; every other default is the current behaviour.

Breaking for non-web consumers: the package now carries
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` for the middleware. Both
consumers are ASP.NET APIs; a console or Functions consumer would need the shared
framework.

## 1.0.0 (2026-05-17)

- Initial .NET 10 port of `trovesuite` (Python).
- `AuthService` — JWT decode, multi-tenant authorization, permission checks.
- `NotificationService` — Gmail SMTP via MailKit (SMS stubbed).
- `StorageService` — Azure Blob with Managed Identity, SAS via user delegation key.
- `Helper` — OTP, unique IDs, JWT encode, activity logging, tenant-aware notifications.
- `DatabaseManager` — Npgsql + Dapper, no ORM, no migrations.
- Health, Auth, Notification, and Storage Minimal API endpoint mappers.
- SQL scripts in `sql/` mirrored from the Python package; package does NOT create tables.
