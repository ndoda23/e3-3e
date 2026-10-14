using CreditFlow.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Repositories;

internal sealed class StaffRepository(CreditFlowDbContext db) : IStaffRepository
{
    public Task<StaffMember?> GetAsync(UserId id, CancellationToken cancellationToken) =>
        db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
        db.UserDirectory.CountAsync(u => u.IsActive && u.RoleIsAdministrative, cancellationToken);

    public void Add(StaffMember staffMember) => db.StaffMembers.Add(staffMember);
}
