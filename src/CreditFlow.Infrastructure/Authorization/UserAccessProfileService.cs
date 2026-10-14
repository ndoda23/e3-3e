using System.ComponentModel;
using CreditFlow.Application.Authorization;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace CreditFlow.Infrastructure.Authorization;

/// <summary>Cached snapshot of a staff member's access-relevant data and account set-up flags.</summary>
[ImmutableObject(true)]
public sealed record CachedStaffAccess(
    bool Exists,
    Guid UserId,
    string FullName,
    Guid RoleId,
    Guid? BranchId,
    string? BranchName,
    bool IsActive,
    bool MustChangePassword,
    bool TwoFactorEnabled,
    bool AccountRequiresMfa)
{
    public static readonly CachedStaffAccess Missing =
        new(false, Guid.Empty, string.Empty, Guid.Empty, null, null, false, false, false, false);
}

/// <summary>Cached role definition with its permissions.</summary>
[ImmutableObject(true)]
public sealed record CachedRoleAccess(
    Guid Id,
    string Code,
    string DisplayName,
    RoleKind Kind,
    DataScope DataScope,
    DashboardKind Dashboard,
    AttributeRule BranchRule,
    bool RequireMfa,
    IReadOnlyList<string> Permissions);

internal static class AccessCacheKeys
{
    public static string User(Guid userId) => $"access:user:{userId:N}";

    public static string Role(Guid roleId) => $"access:role:{roleId:N}";
}

/// <summary>
/// Resolves access profiles and account set-up state from two cache entries: the user's snapshot
/// (<c>access:user:{id}</c>, from <c>vw_UserDirectory</c> in one query) and the role with its permissions
/// (<c>access:role:{id}</c>). A role change therefore affects all its users by removing a single entry.
/// Entries also expire after a short time as a safety net.
/// </summary>
internal sealed class UserAccessProfileService(HybridCache cache, CreditFlowDbContext db) : IUserAccessProfileService
{
    public async Task<UserAccessProfile?> GetAsync(UserId userId, CancellationToken cancellationToken)
    {
        var (staff, role) = await LoadAsync(userId, cancellationToken);
        if (staff is null || role is null)
        {
            return null;
        }

        var profile = new RoleProfile
        {
            Id = new RoleId(role.Id),
            Code = RoleCode.Create(role.Code).Value,
            Kind = role.Kind,
            DataScope = role.DataScope,
            Dashboard = role.Dashboard,
            BranchRule = role.BranchRule,
        };

        return UserAccessProfile.FromParts(
            userId,
            staff.FullName,
            profile,
            role.DisplayName,
            role.Permissions,
            staff.BranchId is { } branchId ? new BranchId(branchId) : null,
            staff.BranchName,
            staff.IsActive);
    }

    public async Task<AccountSecurityState?> GetSecurityStateAsync(UserId userId, CancellationToken cancellationToken)
    {
        var (staff, role) = await LoadAsync(userId, cancellationToken);
        return staff is null || role is null
            ? null
            : new AccountSecurityState(
                staff.MustChangePassword,
                MfaRequired: role.RequireMfa || staff.AccountRequiresMfa,
                MfaEnrolled: staff.TwoFactorEnabled);
    }

    private async Task<(CachedStaffAccess? Staff, CachedRoleAccess? Role)> LoadAsync(
        UserId userId, CancellationToken cancellationToken)
    {
        var staff = await cache.GetOrCreateAsync(
            AccessCacheKeys.User(userId.Value),
            async token => await LoadStaffAsync(userId, token),
            cancellationToken: cancellationToken);
        if (!staff.Exists)
        {
            return (null, null);
        }

        var role = await cache.GetOrCreateAsync(
            AccessCacheKeys.Role(staff.RoleId),
            async token => await LoadRoleAsync(staff.RoleId, token),
            cancellationToken: cancellationToken);
        return (staff, role);
    }

    private async Task<CachedStaffAccess> LoadStaffAsync(UserId userId, CancellationToken cancellationToken)
    {
        var row = await db.UserDirectory.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
        return row is null
            ? CachedStaffAccess.Missing
            : new CachedStaffAccess(
                true,
                row.Id,
                row.FullName,
                row.RoleId,
                row.BranchId,
                row.BranchName,
                row.IsActive,
                row.MustChangePassword,
                row.TwoFactorEnabled,
                row.RequireMfa);
    }

    private async Task<CachedRoleAccess?> LoadRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);

        return role is null
            ? null
            : new CachedRoleAccess(
                role.Id,
                role.Code,
                role.DisplayName,
                role.Kind,
                role.DataScope,
                role.Dashboard,
                role.BranchRule,
                role.RequireMfa,
                role.Permissions.Select(p => p.Permission).Order(StringComparer.Ordinal).ToArray());
    }
}

internal sealed class AccessCacheInvalidator(HybridCache cache) : IAccessCacheInvalidator
{
    public Task InvalidateUserAsync(UserId userId, CancellationToken cancellationToken) =>
        cache.RemoveAsync(AccessCacheKeys.User(userId.Value), cancellationToken).AsTask();

    public Task InvalidateRoleAsync(RoleId roleId, CancellationToken cancellationToken) =>
        cache.RemoveAsync(AccessCacheKeys.Role(roleId.Value), cancellationToken).AsTask();
}
