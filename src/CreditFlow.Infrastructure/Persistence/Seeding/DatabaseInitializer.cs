using CreditFlow.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CreditFlow.Infrastructure.Persistence.Seeding;

/// <summary>Applies migrations (when enabled) and runs the idempotent seeders. Called once at start-up.</summary>
public sealed class DatabaseInitializer(
    CreditFlowDbContext db,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<SeedOptions> seedOptions,
    IHostEnvironment environment,
    IServiceProvider services)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (databaseOptions.Value.ApplyMigrationsOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await services.GetRequiredService<ReferenceDataSeeder>().SeedAsync(cancellationToken);

        var accounts = services.GetRequiredService<AccountSeeder>();
        await accounts.SeedAdministratorAsync(cancellationToken);

        // Demo users and demo applications never reach other environments, whatever the configuration says.
        if (environment.IsDevelopment() && seedOptions.Value.DevUsers.Enabled)
        {
            await accounts.SeedDevelopmentUsersAsync(cancellationToken);
            if (seedOptions.Value.DemoData.Enabled)
            {
                await services.GetRequiredService<DevLendingDataSeeder>().SeedAsync(cancellationToken);
            }
        }
    }
}

public static class DatabaseInitializerExtensions
{
    /// <summary>Runs <see cref="DatabaseInitializer"/> in its own scope.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
    }
}
