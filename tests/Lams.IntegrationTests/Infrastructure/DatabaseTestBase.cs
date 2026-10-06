namespace Lams.IntegrationTests.Infrastructure;

/// <summary>Gives each test class its own migrated and seeded database.</summary>
[Collection(SqlServerCollectionDefinition.Name)]
public abstract class DatabaseTestBase(SqlServerContainer sql) : IAsyncLifetime
{
    protected InfrastructureHost Host { get; private set; } = null!;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected virtual string Environment => "Testing";

    protected virtual bool SeedDevUsers => false;

    public async ValueTask InitializeAsync() =>
        Host = await InfrastructureHost.StartAsync(sql.NewDatabaseConnectionString(), Environment, SeedDevUsers);

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
