using CreditFlow.Application;
using CreditFlow.Infrastructure;
using CreditFlow.Infrastructure.Persistence.Seeding;
using CreditFlow.Web;
using CreditFlow.Web.Logging;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(
    (context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteToMaskedSinks(context.Configuration),
    preserveStaticLogger: true);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWeb();

var app = builder.Build();

// Migrations (when enabled) and idempotent, versioned reference-data seeding.
await app.Services.InitializeDatabaseAsync();

app.UseCreditFlow();

await app.RunAsync();

/// <summary>Entry point; exposed for <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;
