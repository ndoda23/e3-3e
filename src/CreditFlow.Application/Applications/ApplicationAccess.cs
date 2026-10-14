using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Applications;

/// <summary>
/// Which applications a user may see, derived from the access profile (data scope + permission). Every list, inbox,
/// report and by-id read applies it: in SQL for lists (so paging counts are right and nothing invisible is loaded),
/// in memory for a loaded aggregate. Fails closed: inactive users, scope None and Branch scope without a branch see
/// nothing.
/// </summary>
/// <param name="Viewer">The user.</param>
/// <param name="SeesNothing">No application is visible.</param>
/// <param name="Branch">Only applications of this branch are visible; <c>null</c> with
/// <paramref name="SeesNothing"/> false means the whole bank.</param>
public sealed record ApplicationAccessFilter(UserId Viewer, bool SeesNothing, BranchId? Branch)
{
    public static ApplicationAccessFilter For(UserAccessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.HasPermission(Permissions.Applications.View))
        {
            return new(profile.UserId, SeesNothing: true, null);
        }

        return profile.DataScope switch
        {
            DataScope.Organization => new(profile.UserId, SeesNothing: false, null),
            DataScope.Branch when profile.BranchId is { } branch => new(profile.UserId, SeesNothing: false, branch),
            _ => new(profile.UserId, SeesNothing: true, null),
        };
    }

    public bool CanSee(LoanApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return !SeesNothing && (Branch is not { } branch || application.BranchId == branch);
    }
}

/// <summary>The access profile of the signed-in user, for application services.</summary>
public sealed class CurrentAccessProfile(ICurrentUser currentUser, IUserAccessProfileService profiles)
{
    private UserAccessProfile? _profile;

    /// <summary>The profile, or <c>null</c> when nobody (active) is signed in.</summary>
    public async Task<UserAccessProfile?> GetAsync(CancellationToken cancellationToken)
    {
        if (_profile is null && currentUser.UserId is { } id)
        {
            _profile = await profiles.GetAsync(id, cancellationToken);
        }

        return _profile is { IsActive: true } ? _profile : null;
    }

    /// <summary>What the user may decide in underwriting, from permissions; <c>null</c> when they do not decide.</summary>
    public static DeciderAuthority? AuthorityOf(UserAccessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var canOverride = profile.HasPermission(Permissions.Scoring.Override);
        return profile.HasPermission(Permissions.Underwriting.DecideSenior) ? new(ApprovalTier.SeniorUnderwriter, canOverride)
            : profile.HasPermission(Permissions.Underwriting.Decide) ? new(ApprovalTier.Underwriter, canOverride)
            : null;
    }
}
