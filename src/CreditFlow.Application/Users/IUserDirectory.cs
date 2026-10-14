using CreditFlow.Application.Common;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Users;

public enum UserStatusFilter
{
    All = 0,
    Active = 1,
    Inactive = 2,
    LockedOut = 3,

    /// <summary>Active accounts that have not turned on an authenticator.</summary>
    WithoutMfa = 4,
}

public enum UserSortField
{
    Name = 0,
    Role = 1,
    Branch = 2,
    Status = 3,
    LastLogin = 4,
}

public sealed record UserSearchQuery
{
    /// <summary>Matched against full name, user name and email.</summary>
    public string? Search { get; init; }

    public RoleId? RoleId { get; init; }

    public BranchId? BranchId { get; init; }

    public UserStatusFilter Status { get; init; }

    public UserSortField Sort { get; init; }

    public bool Descending { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = Paging.DefaultPageSize;

    public UserSearchQuery Normalize() => this with
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        Status = Enum.IsDefined(Status) ? Status : UserStatusFilter.All,
        Sort = Enum.IsDefined(Sort) ? Sort : UserSortField.Name,
        Page = Paging.NormalizePage(Page),
        PageSize = Paging.NormalizePageSize(PageSize),
    };
}

/// <summary>A row of the Admin › Users list (staff member joined with account, role and branch).</summary>
public sealed record UserListItem(
    UserId Id,
    string FullName,
    string UserName,
    string Email,
    RoleId RoleId,
    string RoleCode,
    string RoleName,
    string? BranchName,
    bool IsActive,
    bool IsAvailable,
    bool IsLockedOut,
    DateTimeOffset? LastLoginAt);

/// <summary>Everything shown on the user details and edit pages.</summary>
public sealed record UserDetails(
    UserId Id,
    string FullName,
    string UserName,
    string Email,
    RoleId RoleId,
    string RoleCode,
    string RoleName,
    bool RoleIsAdministrative,
    bool RoleRequiresMfa,
    BranchId? BranchId,
    string? BranchName,
    bool IsActive,
    bool IsAvailable,
    int Capacity,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    bool TwoFactorEnabled,
    bool RequireMfa,
    bool MustChangePassword,
    DateTimeOffset? TemporaryPasswordExpiresAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Read side of user administration.</summary>
public interface IUserDirectory
{
    Task<PagedResult<UserListItem>> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken);

    Task<UserDetails?> GetDetailsAsync(UserId id, CancellationToken cancellationToken);

    /// <summary>Email addresses of the active users whose role holds <paramref name="permission"/> (notification recipients).</summary>
    Task<IReadOnlyList<string>> ListActiveEmailsWithPermissionAsync(string permission, CancellationToken cancellationToken);
}
