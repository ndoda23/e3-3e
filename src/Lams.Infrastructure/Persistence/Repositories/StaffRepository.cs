using Lams.Application.Users;
using Lams.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Lams.Infrastructure.Persistence.Repositories;

internal sealed class StaffRepository(LamsDbContext db) : IStaffRepository
{
    public Task<StaffMember?> GetAsync(UserId id, CancellationToken cancellationToken) =>
        db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
        db.UserDirectory.CountAsync(u => u.IsActive && u.RoleIsAdministrative, cancellationToken);

    public void Add(StaffMember staffMember) => db.StaffMembers.Add(staffMember);
}
