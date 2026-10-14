using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CreditFlow.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> (migrations do not need a live database). The connection string can be set
/// with the <c>CREDITFLOW_DESIGN_CONNECTION</c> environment variable, e.g. for <c>dotnet ef database update</c>.
/// </summary>
internal sealed class CreditFlowDbContextFactory : IDesignTimeDbContextFactory<CreditFlowDbContext>
{
    public CreditFlowDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CREDITFLOW_DESIGN_CONNECTION")
                               ?? "Server=localhost,1433;Database=CreditFlow;Integrated Security=false;TrustServerCertificate=true";

        var options = new DbContextOptionsBuilder<CreditFlowDbContext>().UseSqlServer(connectionString).Options;
        return new CreditFlowDbContext(options);
    }
}
