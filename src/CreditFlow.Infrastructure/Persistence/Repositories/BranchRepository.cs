using CreditFlow.Domain.Branches;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Repositories;

internal sealed class BranchRepository(CreditFlowDbContext db) : IBranchRepository
{
    public Task<Branch?> GetAsync(BranchId id, CancellationToken cancellationToken) =>
        db.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        db.Branches.AnyAsync(b => b.Code == code, cancellationToken);

    public Task<int> CountAssignedStaffAsync(BranchId id, CancellationToken cancellationToken) =>
        db.StaffMembers.CountAsync(s => s.BranchId == id, cancellationToken);

    public void Add(Branch branch) => db.Branches.Add(branch);

    public void Remove(Branch branch) => db.Branches.Remove(branch);
}
