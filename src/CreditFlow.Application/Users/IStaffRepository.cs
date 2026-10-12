using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Users;

public interface IStaffRepository
{
    Task<StaffMember?> GetAsync(UserId id, CancellationToken cancellationToken);

    /// <summary>Active staff members whose role is administrative.</summary>
    Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken);

    void Add(StaffMember staffMember);
}
