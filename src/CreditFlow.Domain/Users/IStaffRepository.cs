namespace CreditFlow.Domain.Users;

/// <summary>Persistence of the <see cref="StaffMember"/> aggregate.</summary>
public interface IStaffRepository
{
    Task<StaffMember?> GetAsync(UserId id, CancellationToken cancellationToken);

    /// <summary>Active staff members whose role is administrative (for <see cref="AdminContinuityPolicy"/>).</summary>
    Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken);

    void Add(StaffMember staffMember);
}
