namespace Trovesuite.Package.Configuration;

public sealed class TrovesuiteOptions
{
    public const string SectionName = "Trovesuite";

    public DatabaseOptions Database { get; set; } = new();
    public JwtOptions Jwt { get; set; } = new();
    public MailOptions Mail { get; set; } = new();
    public AzureStorageOptions AzureStorage { get; set; } = new();
    public CorePlatformTableOptions Tables { get; set; } = new();
    public AppOptions App { get; set; } = new();
    public Tenancy.TenancyOptions Tenancy { get; set; } = new();
}

public sealed class DatabaseOptions
{
    public string? ConnectionString { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; } = 5432;
    public string? Database { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int MaxPoolSize { get; set; } = 5;
    public int MinPoolSize { get; set; } = 1;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public string? ApplicationName { get; set; }

    public string BuildConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
            return ConnectionString;

        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = Host ?? "localhost",
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password,
            MaxPoolSize = MaxPoolSize,
            MinPoolSize = MinPoolSize,
            CommandTimeout = CommandTimeoutSeconds,
            ApplicationName = ApplicationName ?? "Trovesuite.Package",
            KeepAlive = 30,
        };
        return builder.ToString();
    }
}

public sealed class JwtOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string Algorithm { get; set; } = "HS256";
    public int AccessTokenExpireMinutes { get; set; } = 120;
}

public sealed class MailOptions
{
    public string? SenderEmail { get; set; }
    public string? SenderPassword { get; set; }
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 465;
    public bool UseSsl { get; set; } = true;
}

public sealed class AzureStorageOptions
{
    public string? AccountName { get; set; }
    public string? UserAssignedManagedIdentityClientId { get; set; }
}

public sealed class AppOptions
{
    public string Name { get; set; } = "Trovesuite";
    public string Version { get; set; } = "1.0.0";
    public string Environment { get; set; } = "development";
    public bool Debug { get; set; }
    public string AppUrl { get; set; } = "https://trovesuite.com";
}

public sealed class CorePlatformTableOptions
{
    public string Tenants { get; set; } = "core_platform.cp_tenants";
    public string Subscriptions { get; set; } = "core_platform.cp_subscriptions";
    public string Apps { get; set; } = "core_platform.cp_apps";
    public string Users { get; set; } = "core_platform.cp_users";
    public string ResourceTypes { get; set; } = "core_platform.cp_resource_types";
    public string ResourceIds { get; set; } = "core_platform.cp_resource_ids";
    public string Permissions { get; set; } = "core_platform.cp_permissions";
    public string Roles { get; set; } = "core_platform.cp_roles";
    public string RolePermissions { get; set; } = "core_platform.cp_role_permissions";
    public string Actions { get; set; } = "core_platform.cp_actions";
    public string AppSubscriptions { get; set; } = "core_platform.cp_app_subscriptions";
    public string AppSubscriptionHistories { get; set; } = "core_platform.cp_app_subscription_histories";
    public string AppTierConfigs { get; set; } = "core_platform.cp_app_tier_configs";
    public string Otps { get; set; } = "core_platform.cp_otps";
    public string PasswordPolicies { get; set; } = "core_platform.cp_password_policies";
    public string MultiFactorSettings { get; set; } = "core_platform.cp_multi_factor_settings";
    public string UserLoginTracking { get; set; } = "core_platform.cp_user_login_tracking";
    public string EnterpriseSubscriptions { get; set; } = "core_platform.cp_enterprise_subscriptions";
    public string ChangePasswordPolicy { get; set; } = "core_platform.cp_change_password_policy";
    public string AppFeatures { get; set; } = "core_platform.cp_app_features";

    public string Groups { get; set; } = "core_platform.cp_groups";
    public string LoginSettings { get; set; } = "core_platform.cp_login_settings";
    public string Resources { get; set; } = "core_platform.cp_resources";
    public string AssignRoles { get; set; } = "core_platform.cp_assign_roles";
    public string ResourceDeletionChatHistories { get; set; } = "core_platform.cp_resource_deletion_chat_histories";
    public string UserGroups { get; set; } = "core_platform.cp_user_groups";
    public string ActivityLogs { get; set; } = "core_platform.cp_activity_logs";
    public string Organizations { get; set; } = "core_platform.cp_organizations";
    public string Businesses { get; set; } = "core_platform.cp_businesses";
    public string BusinessApps { get; set; } = "core_platform.cp_business_apps";
    public string Locations { get; set; } = "core_platform.cp_locations";
    public string AssignLocations { get; set; } = "core_platform.cp_assign_locations";
    public string UnitOfMeasures { get; set; } = "core_platform.cp_unit_of_measures";
    public string Currencies { get; set; } = "core_platform.cp_currencies";
    public string Themes { get; set; } = "core_platform.cp_themes";
    public string NotificationEmailCredentials { get; set; } = "core_platform.cp_notification_email_credentials";
}
