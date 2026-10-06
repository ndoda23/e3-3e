using Lams.Domain.Roles;

namespace Lams.Application.Authorization;

/// <summary>Decides whether a record falls within a user's data scope.</summary>
public interface IDataScopeEvaluator
{
    bool IsInScope(UserAccessProfile user, IScopedResource resource);
}

/// <summary>
/// Data scope check. Fails closed: an inactive user, scope <see cref="DataScope.None"/>, or a scope whose user
/// attribute is missing (no branch for Branch scope, no segment for Segment scope) sees nothing.
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
            DataScope.Own => resource.OwnerUserId == user.UserId,
            DataScope.Assigned => resource.AssignedUserId == user.UserId,
            DataScope.Branch => user.BranchId is { } branch && resource.BranchId == branch,
            DataScope.Segment => user.Segment is { } segment && resource.Segment == segment,
            DataScope.Organization => true,
            _ => false,
        };
    }
}
