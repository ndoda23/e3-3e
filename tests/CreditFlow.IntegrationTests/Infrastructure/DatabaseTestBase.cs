using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Infrastructure;

/// <summary>Gives each test class its own migrated and seeded database.</summary>
[Collection(SqlServerCollectionDefinition.Name)]
public abstract class DatabaseTestBase(SqlServerContainer sql) : IAsyncLifetime
{
    protected InfrastructureHost Host { get; private set; } = null!;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected virtual string Environment => "Testing";

    protected virtual bool SeedDevUsers => false;

    /// <summary>A controllable clock for the host, or <c>null</c> for the system clock.</summary>
    protected virtual TimeProvider? Clock => null;

    protected virtual IReadOnlyDictionary<string, string?>? Settings => null;

    public async ValueTask InitializeAsync() =>
        Host = await InfrastructureHost.StartAsync(
            sql.NewDatabaseConnectionString(), Environment, SeedDevUsers, Clock, Settings, ConfigureServices);

    /// <summary>Replaces services before the host starts (none by default).</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
