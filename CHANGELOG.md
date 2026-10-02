# Changelog

## 1.0.9 (2026-10-02)

Two bugs in 1.0.8's silo connections, both found by the first real silo request
to a .NET app on dev, neither reachable by building.

- **The secret is a libpq URI and Npgsql does not take one.** Every `db-url-*`
  secret is `postgresql://user:pass@host:5432/db?sslmode=require`, because that
  is what psycopg2 reads natively and the Python apps read the same secrets.
  `NpgsqlDataSourceBuilder` wants keyword/value pairs and, handed a URI, reads
  the whole string as one keyword: `ArgumentException: Couldn't set
  postgresql://… ---> KeyNotFoundException`. So every silo request was refused
  with 503. `Tenancy/PostgresUri` normalises it, and passes a keyword/value
  string through untouched.
- **The failure published the tenant's database password.** Npgsql puts the
  offending connection string in its `ArgumentException`, so the message — and
  the inner exception, since logging an exception walks the whole chain — printed
  the credential into the container log. Both are now dropped; the message keeps
  the database, the server and the exception type, none of which is a secret.

## 1.0.8 (2026-10-02)

A connection follows the route, mirroring `trovesuite` 1.0.56. Until now the
.NET apps resolved WHICH tenant a request belonged to and then connected to the
pod's configured database anyway — correct while every route was POOLED and
there was one database, and a cross-tenant read the moment a silo existed.

- `Tenancy/SiloDataSources` — one Npgsql data source per silo credential, keyed
  on the secret reference rather than the host, because several hosts can address
  one silo and one host must not end up with several pools.
- `TenantRoute.SiloKey` and `TenancyOptions.DbSecretPrefix` / `KeyVaultUri`.
  **Each app resolves its own credential**: a silo has a login role per app, so
  the row names the silo and the app names itself —
  `db-url-<app-slug>-<silo_key>`, the pooled name plus a suffix. A leaked
  credential is then one app's access to one tenant rather than every app's to
  all of them. A row carrying a single `DbSecretUri` is still honoured.
- The prefix is configuration, not derivation. Nothing in a running container
  spells the slug the way the secret does — these apps set no app name at all,
  and core-platform's is `core-platform` where its secret says `coreplatform` —
  so the IaC that creates the secret supplies it. Guessing would give four apps
  that work and one that does not.
- `NpgsqlConnectionFactory` consults the route on both overloads, and
  `DataSourceForCurrentRoute()` serves EF Core.
- The middleware calls `PrepareForRouteAsync` before any handler runs. EF builds
  its options synchronously and the apps' own helpers are synchronous, so neither
  can read a Key Vault secret when it needs one; paying for it once per request
  makes those paths a dictionary lookup. A failure there refuses the request with
  503 rather than letting it reach a handler that would fall back.
- `ForgetAsync` / `ForgetSiloAsync`, because a composed reference carries no
  secret VERSION: the name is unchanged across a rotation, so the stale pool
  would otherwise be reused with the old password until the process restarted.

**It fails closed throughout.** Unset configuration, an unreadable vault, a silo
route with nothing prepared — all throw `SiloUnavailableException`. None of them
falls back to the pod's own database, because that serves one tenant from the
shared one and looks like success.

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
