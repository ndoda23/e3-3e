using CreditFlow.Application;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.Infrastructure.Persistence.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CreditFlow.IntegrationTests.Infrastructure;

/// <summary>Mutable current user for tests (the acting administrator).</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public UserId? UserId { get; set; }

    public string? UserName { get; set; }

    public string? IpAddress => "10.0.0.1";

    public string? UserAgent => "integration-tests";

    public string? CorrelationId => "it";
}

/// <summary>
/// The Application and Infrastructure layers wired exactly as in production, against a fresh database:
/// migrated and seeded on <see cref="StartAsync"/>.
/// </summary>
public sealed class InfrastructureHost : IAsyncDisposable
{
    public const string AdminUserName = "sysadmin";
    public const string AdminPassword = "Adm1n!Initial-Pass";
    public const string DevPassword = "Dev!Passw0rd-2026";

    private readonly ServiceProvider _provider;

    private InfrastructureHost(ServiceProvider provider) => _provider = provider;

    public IServiceProvider Services => _provider;

    public TestCurrentUser CurrentUser => _provider.GetRequiredService<TestCurrentUser>();

    public static async Task<InfrastructureHost> StartAsync(
        string connectionString, string environment = "Testing", bool seedDevUsers = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CreditFlow"] = connectionString,
                ["Database:ApplyMigrationsOnStartup"] = "true",
                ["Seed:Admin:UserName"] = AdminUserName,
                ["Seed:Admin:Email"] = "sysadmin@pcb.ge",
                ["Seed:Admin:Password"] = AdminPassword,
                ["Seed:DevUsers:Enabled"] = seedDevUsers ? "true" : "false",
                ["Seed:DevUsers:Password"] = DevPassword,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddApplication();
        services.AddInfrastructure(configuration);

        services.AddSingleton<TestCurrentUser>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser>(sp => sp.GetRequiredService<TestCurrentUser>()));

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.InitializeDatabaseAsync(TestContext.Current.CancellationToken);
        return new InfrastructureHost(provider);
    }

    /// <summary>Runs <paramref name="action"/> in a new DI scope (like one HTTP request).</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> action) =>
        InScopeAsync<bool>(async sp =>
        {
            await action(sp);
            return true;
        });

    /// <summary>Reads with a fresh context (no tracked state).</summary>
    public Task<T> QueryAsync<T>(Func<CreditFlowDbContext, Task<T>> query) =>
        InScopeAsync(sp => query(sp.GetRequiredService<CreditFlowDbContext>()));

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "CreditFlow.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
