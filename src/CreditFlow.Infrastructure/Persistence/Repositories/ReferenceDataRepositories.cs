using CreditFlow.Application.Branches;
using CreditFlow.Domain.Branches;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Repositories;

internal sealed class BranchRepository(CreditFlowDbContext db) : IBranchRepository
{
    public Task<Branch?> GetAsync(BranchId id, CancellationToken cancellationToken) =>
        db.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BranchSummary>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var branches = await db.Branches.AsNoTracking()
            .Where(b => includeInactive || b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        var counts = await db.StaffMembers
            .Where(s => s.BranchId != null)
            .GroupBy(s => s.BranchId)
            .Select(g => new { BranchId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var countById = counts.ToDictionary(c => c.BranchId!.Value, c => c.Count);

        return branches
            .Select(b => new BranchSummary(b.Id, b.Code, b.Name, b.City, b.IsActive, countById.GetValueOrDefault(b.Id)))
            .ToList();
    }

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        db.Branches.AnyAsync(b => b.Code == code, cancellationToken);

    public Task<int> CountAssignedStaffAsync(BranchId id, CancellationToken cancellationToken) =>
        db.StaffMembers.CountAsync(s => s.BranchId == id, cancellationToken);

    public void Add(Branch branch) => db.Branches.Add(branch);

    public void Remove(Branch branch) => db.Branches.Remove(branch);
}
