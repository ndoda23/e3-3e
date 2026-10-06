using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lams.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef</c> (migrations do not need a live database). The connection string can be set
/// with the <c>LAMS_DESIGN_CONNECTION</c> environment variable, e.g. for <c>dotnet ef database update</c>.
/// </summary>
internal sealed class LamsDbContextFactory : IDesignTimeDbContextFactory<LamsDbContext>
{
    public LamsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LAMS_DESIGN_CONNECTION")
                               ?? "Server=localhost,1433;Database=Lams;Integrated Security=false;TrustServerCertificate=true";

        var options = new DbContextOptionsBuilder<LamsDbContext>().UseSqlServer(connectionString).Options;
        return new LamsDbContext(options);
    }
}
