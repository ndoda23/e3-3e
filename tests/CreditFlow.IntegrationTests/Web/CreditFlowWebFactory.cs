using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>
/// The real web application (Program.cs) against its own SQL Server database, with a controllable clock.
/// Clients use https (the auth cookies are <c>__Host-</c> and Secure) and do not follow redirects.
/// </summary>
public sealed class CreditFlowWebFactory(
    string connectionString,
    string environment = "Testing",
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero));

    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:CreditFlow"] = connectionString,
                ["Database:ApplyMigrationsOnStartup"] = "true",
                ["Seed:Admin:UserName"] = InfrastructureHost.AdminUserName,
                ["Seed:Admin:Email"] = "sysadmin@pcb.ge",
                ["Seed:Admin:Password"] = InfrastructureHost.AdminPassword,
                ["Seed:DevUsers:Enabled"] = "false",
                ["Serilog:MinimumLevel:Default"] = "Warning",

                // Tests sign in many times from the same (test) client; the rate limit has its own test.
                ["Security:SignInAttemptsPerMinute"] = "1000",
            }.Concat(settings ?? new Dictionary<string, string?>()).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value)));

        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock)));
    }
}
