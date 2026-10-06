using Lams.Application;
using Lams.Infrastructure;
using Lams.Infrastructure.Persistence.Seeding;
using Lams.Web;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(
    (context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext(),
    preserveStaticLogger: true);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWeb();

var app = builder.Build();

// Migrations (when enabled) and idempotent, versioned reference-data seeding.
await app.Services.InitializeDatabaseAsync();

app.UseLams();

await app.RunAsync();

/// <summary>Entry point; exposed for <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;
