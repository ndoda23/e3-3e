using CreditFlow.Application.Branches;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Queries;

internal sealed class BranchQueries(CreditFlowDbContext db) : IBranchQueries
{
    /// <summary>One query: each branch with its assigned-staff count as a correlated sub-query.</summary>
    public async Task<IReadOnlyList<BranchSummary>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        await db.Branches.AsNoTracking()
            .Where(b => includeInactive || b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BranchSummary(
                b.Id, b.Code, b.Name, b.City, b.IsActive, db.StaffMembers.Count(s => s.BranchId == b.Id)))
            .ToListAsync(cancellationToken);
}
