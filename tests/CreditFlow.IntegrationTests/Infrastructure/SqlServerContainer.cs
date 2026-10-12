using Testcontainers.MsSql;

namespace CreditFlow.IntegrationTests.Infrastructure;

/// <summary>One SQL Server container shared by all integration tests; each test class uses its own database.</summary>
public sealed class SqlServerContainer : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>A connection string for a new, uniquely named database (created by migrations on first use).</summary>
    public string NewDatabaseConnectionString()
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"creditflow_test_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        };
        return builder.ConnectionString;
    }

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollectionDefinition : ICollectionFixture<SqlServerContainer>
{
    public const string Name = "SQL Server";
}
