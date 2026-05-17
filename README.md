# Trovesuite.Package (.NET 10)

Shared service layer for TroveSuite applications, ported from the Python `trovesuite` package. Bundles auth (JWT + multi-tenant authorization), email notifications, Azure Blob Storage, and a Helper utility — wired up for ASP.NET Core via dependency injection.

> This package is built for the TroveSuite platform and assumes the TroveSuite `core_platform` PostgreSQL schema (tenants, users, groups, roles, permissions). It is **not** a general-purpose auth library.

## Important: the package does NOT create or migrate the database

This package queries an existing `core_platform` schema — it never issues DDL itself and it does not bundle migration scripts. The schema is the responsibility of [`tvs-sqlscript`](../tvs-sqlscript), our EF Core migration runner, which is the single source of truth for `core_platform` and the per-app schemas (loan_drift, my_store_guard, human_resource).

Apply the migrations once (per environment) before any consumer of this package starts up:

```bash
cd ../tvs-sqlscript
dotnet run --project src/Trovesuite.Database.Runner -- deploy --module all
```

See [`tvs-sqlscript/README.md`](../tvs-sqlscript/README.md) for the full multi-tenant deployment flow.

## What this is — and is not

`Trovesuite.Package` is a **pure library** (no ASP.NET dependency). It gives you a set of services that you DI-register into any .NET 10 app — Web API, worker, console, BackgroundService, anything that uses `Microsoft.Extensions.DependencyInjection`. It does not ship HTTP endpoints, controllers, or middleware.

If you want a ready-to-run Web API that already wires everything up, use the [`tvs-dotnet-template`](../tvs-dotnet-template) `dotnet new` template — it scaffolds a WebAPI project that references this package and exposes the standard endpoints (`/auth`, `/health`, `/send_email`, `/storage/*`).

## Install

Pack and consume the NuGet:

```bash
dotnet pack src/Trovesuite.Package/Trovesuite.Package.csproj -c Release
dotnet add <YourProject> package Trovesuite.Package --source <local-or-remote-feed>
```

The package targets `net10.0` and brings in:

- `Microsoft.Extensions.*` — options, DI, configuration, logging abstractions.
- `Npgsql` + `Dapper` — Postgres access, no ORM.
- `System.IdentityModel.Tokens.Jwt` — JWT encode/decode.
- `MailKit` — SMTP email.
- `Azure.Storage.Blobs` + `Azure.Identity` — blob storage via Managed Identity.

## Configure

Bind options from `IConfiguration` under the `Trovesuite` section:

```json
{
  "Trovesuite": {
    "Database": {
      "ConnectionString": "Host=localhost;Port=5432;Database=trovesuite;Username=postgres;Password=postgres"
    },
    "Jwt": {
      "SecretKey": "change-me",
      "Algorithm": "HS256",
      "AccessTokenExpireMinutes": 60
    },
    "Mail": {
      "SenderEmail": "alerts@yourdomain.com",
      "SenderPassword": "gmail-app-password",
      "SmtpHost": "smtp.gmail.com",
      "SmtpPort": 465,
      "UseSsl": true
    },
    "AzureStorage": {
      "AccountName": "yourstorageaccount",
      "UserAssignedManagedIdentityClientId": null
    },
    "App": {
      "Name": "Trovesuite",
      "Environment": "production",
      "AppUrl": "https://app.trovesuite.com"
    }
  }
}
```

Then wire up DI (works in any host — WebApi, Worker, Console, etc.):

```csharp
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Auth;
using Trovesuite.Package.Notification;
using Trovesuite.Package.Storage;
using Trovesuite.Package.Utils;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTrovesuite(builder.Configuration);

var app = builder.Build();
// Resolve services anywhere you need them:
var auth = app.Services.GetRequiredService<IAuthService>();
var storage = app.Services.GetRequiredService<IStorageService>();
```

Or configure inline (no `appsettings.json`):

```csharp
builder.Services.AddTrovesuite(options =>
{
    options.Database.ConnectionString = "Host=localhost;...";
    options.Jwt.SecretKey = "change-me";
});
```

> Need ready-made HTTP endpoints (`/auth`, `/health`, `/send_email`, `/storage/*`)? Don't write them by hand — scaffold a project from the [`tvs-dotnet-template`](../tvs-dotnet-template). It already does this in `Program.cs`.

## Usage

### Authorize a user from a JWT

```csharp
public sealed class ProtectedController(IAuthService auth)
{
    public async Task<IResult> Handle(string token)
    {
        var result = await auth.AuthorizeUserFromTokenAsync(token);
        if (!result.Success)
            return Results.Json(result, statusCode: result.StatusCode);

        foreach (var entry in result.Data!)
            Console.WriteLine($"{entry.RoleId} -> [{string.Join(",", entry.Permissions ?? new())}]");

        return Results.Ok(result);
    }
}
```

### Authorize by IDs

```csharp
var result = await auth.AuthorizeAsync(new AuthServiceWriteDto
{
    UserId = "usr_...",
    TenantId = "tnt_...",
});
```

`AuthorizeAsync` verifies the tenant is verified, applies login-window restrictions (working days OR a time period), merges tenant-level and system-level roles, and returns `Respons<AuthServiceReadDto>` with each role's permissions attached.

Possible error codes: `INVALID_USER_ID`, `INVALID_TENANT_ID`, `TENANT_NOT_FOUND`, `TENANT_NOT_VERIFIED`, `USER_NOT_FOUND`, `USER_SUSPENDED`, `LOGIN_TIME_RESTRICTED`, `LOGIN_DAY_RESTRICTED`.

### Check permissions

```csharp
bool canCreate = auth.CheckPermission(result.Data!, action: "permission-user-create");
bool canCreateInGroup = auth.CheckPermission(result.Data!, action: "permission-user-create", resourceType: "rt-group");

var all = auth.GetUserPermissions(result.Data!);
auth.HasAnyPermission(result.Data!, new[] { "p-read", "p-write" });
auth.HasAllPermissions(result.Data!, new[] { "p-read", "p-write" });
```

### Send an email

```csharp
public sealed class Sender(INotificationService notify)
{
    public Task<Respons<NotificationEmailServiceReadDto>> Welcome(string to) =>
        notify.SendEmailAsync(new NotificationEmailServiceWriteDto
        {
            SenderEmail = "alerts@yourdomain.com",
            ReceiverEmail = new() { to },
            Password = "gmail-app-password",
            Subject = "Welcome",
            TextMessage = "Plain text body",
            HtmlMessage = "<h1>HTML body</h1>",
        });
}
```

For tenant-aware sending that pulls credentials from `core_platform.cp_notification_email_credentials` (and falls back to `Trovesuite:Mail:SenderEmail` / `SenderPassword`), use `IHelper.SendNotificationAsync(...)` with a template from `Trovesuite.Package.Utils.Templates`.

### Azure Blob Storage

`StorageService` authenticates via Managed Identity (user-assigned if a client id is provided, otherwise `DefaultAzureCredential`). SAS URLs are issued via user-delegation keys — no storage account key required.

```csharp
public sealed class Files(IStorageService storage)
{
    public Task<Respons<StorageFileUploadServiceReadDto>> UploadInvoice(byte[] pdf) =>
        storage.UploadFileAsync(new StorageFileUploadServiceWriteDto
        {
            StorageAccountUrl = "https://myaccount.blob.core.windows.net",
            ContainerName = "documents",
            BlobName = "invoice.pdf",
            DirectoryPath = "tenants/tnt_abc",
            FileContent = pdf,
            ContentType = "application/pdf",
        });

    public Task<Respons<StorageFileUrlServiceReadDto>> GetInvoiceUrl() =>
        storage.GetFileUrlAsync(new StorageFileUrlServiceWriteDto
        {
            StorageAccountUrl = "https://myaccount.blob.core.windows.net",
            ContainerName = "documents",
            BlobName = "tenants/tnt_abc/invoice.pdf",
            ExpiryHours = 2,
        });
}
```

Also available on `IStorageService`: `CreateContainerAsync`, `UpdateFileAsync`, `DeleteFileAsync`, `DeleteMultipleFilesAsync`, `DownloadFileAsync`.

## Response shape

Every service method returns `Respons<T>` (typo preserved from the Python contract so JSON wire format matches both clients):

```csharp
public class Respons<T>
{
    public string? Detail { get; set; }
    public string? Error { get; set; }
    public List<T>? Data { get; set; }
    public int StatusCode { get; set; } = 200;
    public bool Success { get; set; } = true;
    public PaginationMeta? Pagination { get; set; }
}
```

Always branch on `result.Success` before reading `result.Data`.

## Database expectations

`AuthService` and `Helper` query the TroveSuite `core_platform` schema. Table names are overridable via `Trovesuite:Tables:*`:

- `Tenants` → `core_platform.cp_tenants`
- `Users` → `core_platform.cp_users`
- `Roles` → `core_platform.cp_roles`
- `RolePermissions` → `core_platform.cp_role_permissions`
- `AssignRoles` → `core_platform.cp_assign_roles`
- `UserGroups` → `core_platform.cp_user_groups`
- `LoginSettings` → `core_platform.cp_login_settings`
- `ActivityLogs` → `core_platform.cp_activity_logs`
- `ResourceIds` → `core_platform.cp_resource_ids`
- `NotificationEmailCredentials` → `core_platform.cp_notification_email_credentials`

See [`Configuration/TrovesuiteOptions.cs`](src/Trovesuite.Package/Configuration/TrovesuiteOptions.cs) for the full list.

The columns referenced in [`Auth/AuthService.cs`](src/Trovesuite.Package/Auth/AuthService.cs) (notably `delete_status`, `is_active`, `is_system`, `is_suspended`, `working_days`, `login_on`, `logout_on`, `resource_type`) must exist on the matching tables.

## Development

```bash
cd tvs-package-dotnet
dotnet build
dotnet test           # once tests are added
```

## Releasing

1. Bump `<Version>` in [`src/Trovesuite.Package/Trovesuite.Package.csproj`](src/Trovesuite.Package/Trovesuite.Package.csproj).
2. `dotnet pack -c Release` produces a `.nupkg` under `bin/Release/`.
3. Push to your NuGet feed (`dotnet nuget push ...`).

## License

MIT — see [LICENSE](LICENSE).
