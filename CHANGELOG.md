# Changelog

## 1.0.7 (2026-10-02)

`Tenancy/DatabaseFanout` — running background work once per DATABASE, mirroring
`trovesuite.tenancy.for_each_database` on the Python side so both halves of the
suite convert the same way.

Not once per tenant, which was the obvious reading and is wrong: the route table
enumerates addresses, every pooled row has a null `TenantId` because a pooled
address serves all tenants, and the token says which. Within a database the
existing query is already right.

- `ActiveDatabaseScopesAsync` — one route per distinct (server, database), with
  both null meaning "the database this pod was configured with".
- `ForEachDatabaseAsync` — the loop, with the try/catch outside the scope and
  inside the loop so one unreachable database does not stop the others, and a
  fallback that runs the work **once** unscoped if the control plane cannot be
  read at all. Work that silently stops because a lookup failed is worse than the
  failure it would be guarding against.

## 1.0.6 (2026-10-01)

Wildcard parent routes, matching `trovesuite` 1.0.53 on the Python side.

- A `ctl_tenant_routes` row flagged `is_wildcard` also answers for every subdomain
  of its host, so the employee portal's `<company>.dev.zeloshr.com` resolves
  without a row per portal. An exact host always wins, which is what makes a
  tenant moving to its own database a one-row change rather than a backfill.
- `TenantRoute.IsWildcard`, `TenantRoute.RequestedHost` and
  `TenantRoute.AnsweredByParent`. `Host` says which row decided, `RequestedHost`
  says what the browser sent.
- `TenantRouteResolver.Parents(host)` is public, like `NormaliseHost`: a pure
  function stating a documented rule, worth checking from outside the package.
- The lookup is one query over the host plus its parent domains, ordered
  longest-first. `host = ANY(...)` and not `LIKE '%.' || host`, because a stored
  host may contain an underscore, which LIKE reads as "any character".

Requires migration `20261001-04-a-parent-domain-may-claim-its-subdomains.sql`.
Without the column the lookup fails, the resolver warns once and treats every host
as unresolved — harmless while `Trovesuite:Tenancy:Enforce` is off, an outage if it
is on. The deploy pipelines migrate before they roll the image.

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
