using CreditFlow.Application.AuthorityLimits;
using CreditFlow.Application.Branches;
using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
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

internal sealed class AuthorityLimitRepository(CreditFlowDbContext db) : IAuthorityLimitRepository
{
    public async Task<IReadOnlyList<AuthorityLimit>> ListAsync(CancellationToken cancellationToken) =>
        await db.AuthorityLimits.ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AuthorityLimit>> ListForRoleAsync(RoleId roleId, CancellationToken cancellationToken) =>
        await db.AuthorityLimits.AsNoTracking().Where(l => l.RoleId == roleId).ToListAsync(cancellationToken);

    public Task<AuthorityLimit?> GetAsync(AuthorityLimitId id, CancellationToken cancellationToken) =>
        db.AuthorityLimits.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<bool> CreditDecisionLimitExistsAsync(
        RoleId roleId, AuthorityLevel? authorityLevel, Currency currency, CancellationToken cancellationToken) =>
        db.AuthorityLimits.AnyAsync(
            l => l.RoleId == roleId
                 && l.LimitType == LimitType.CreditDecision
                 && l.AuthorityLevel == authorityLevel
                 && l.Currency == currency,
            cancellationToken);

    public Task<bool> PricingExceptionLimitExistsAsync(RoleId roleId, CancellationToken cancellationToken) =>
        db.AuthorityLimits.AnyAsync(
            l => l.RoleId == roleId && l.LimitType == LimitType.PricingException, cancellationToken);

    public void Add(AuthorityLimit limit) => db.AuthorityLimits.Add(limit);

    public void Remove(AuthorityLimit limit) => db.AuthorityLimits.Remove(limit);
}
