using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trovesuite.Package.Auth;
using Trovesuite.Package.Database;
using Trovesuite.Package.Notification;
using Trovesuite.Package.Storage;
using Trovesuite.Package.Tenancy;
using Trovesuite.Package.Utils;

namespace Trovesuite.Package.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrovesuite(this IServiceCollection services, IConfiguration configuration, string sectionName = TrovesuiteOptions.SectionName)
    {
        services.Configure<TrovesuiteOptions>(configuration.GetSection(sectionName));
        return services.AddTrovesuiteCore();
    }

    public static IServiceCollection AddTrovesuite(this IServiceCollection services, Action<TrovesuiteOptions> configure)
    {
        services.Configure(configure);
        return services.AddTrovesuiteCore();
    }

    private static IServiceCollection AddTrovesuiteCore(this IServiceCollection services)
    {
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<IDatabaseManager, DatabaseManager>();
        services.AddSingleton<IHelper, Helper>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IStorageService, StorageService>();
        // Singleton because it owns a process-wide cache; the middleware that
        // uses it is per-request but holds no state of its own.
        services.AddSingleton<ITenantRouteResolver, TenantRouteResolver>();
        return services;
    }
}
