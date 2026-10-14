using CreditFlow.Domain.Roles;

namespace CreditFlow.Application.Authorization;

/// <summary>Decides whether a record falls within a user's data scope.</summary>
public interface IDataScopeEvaluator
{
    bool IsInScope(UserAccessProfile user, IScopedResource resource);
}

/// <summary>
/// Data scope check for a single loaded record (lists filter in SQL with the same rules). Fails closed: an inactive
/// user, scope <see cref="DataScope.None"/>, or Branch scope without a branch sees nothing.
/// </summary>
public sealed class DataScopeEvaluator : IDataScopeEvaluator
{
    public bool IsInScope(UserAccessProfile user, IScopedResource resource)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(resource);

        if (!user.IsActive)
        {
            return false;
        }

        return user.DataScope switch
        {
            DataScope.Branch => user.BranchId is { } branch && resource.BranchId == branch,
            DataScope.Organization => true,
            _ => false,
        };
    }
}
