using System.ComponentModel.DataAnnotations;
using CreditFlow.Application.Auditing;
using CreditFlow.Application.Branches;
using CreditFlow.Application.Common;
using CreditFlow.Application.Roles;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace CreditFlow.Web.Areas.Admin.Models;

/// <summary>A role in a drop-down, with the branch rule that decides whether the user form shows the branch.</summary>
public sealed record RoleOption(Guid Id, string Code, string Name, AttributeRule BranchRule, bool IsAdministrative)
{
    public static RoleOption From(RoleDetails role)
    {
        ArgumentNullException.ThrowIfNull(role);
        var p = role.Profile;
        return new(p.Id.Value, p.Code.Value, role.DisplayName, p.BranchRule, p.IsAdministrative);
    }
}

public sealed record BranchOption(Guid Id, string Name, bool IsActive)
{
    public static BranchOption From(BranchSummary branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return new(branch.Id.Value, branch.Name, branch.IsActive);
    }
}

/// <summary>Filters, sort and page of the user list, bound from the query string (also used to build links).</summary>
public sealed class UserListQuery
{
    public string? Search { get; set; }

    public Guid? Role { get; set; }

    public Guid? Branch { get; set; }

    public UserStatusFilter Status { get; set; }

    public UserSortField Sort { get; set; }

    public bool Desc { get; set; }

    public int Page { get; set; } = 1;

    public UserSearchQuery ToSearch() => new UserSearchQuery
    {
        Search = Search,
        RoleId = Role is { } role ? new RoleId(role) : null,
        BranchId = Branch is { } branch ? new CreditFlow.Domain.Branches.BranchId(branch) : null,
        Status = Status,
        Sort = Sort,
        Descending = Desc,
        Page = Page,
    }.Normalize();

    /// <summary>Route values for a link to the list with the current filters (defaults are left out).</summary>
    public RouteValueDictionary RouteValues(UserSortField? sort = null, bool? desc = null, int? page = null)
    {
        var values = new RouteValueDictionary();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            values["search"] = Search.Trim();
        }

        if (Role is { } role)
        {
            values["role"] = role;
        }

        if (Branch is { } branch)
        {
            values["branch"] = branch;
        }

        if (Status != UserStatusFilter.All)
        {
            values["status"] = Status;
        }

        var sortField = sort ?? Sort;
        if (sortField != UserSortField.Name)
        {
            values["sort"] = sortField;
        }

        if (desc ?? Desc)
        {
            values["desc"] = "true";
        }

        var pageNumber = page ?? Page;
        if (pageNumber > 1)
        {
            values["page"] = pageNumber;
        }

        return values;
    }
}

public sealed record UserIndexViewModel(
    UserListQuery Query,
    PagedResult<UserListItem> Users,
    IReadOnlyList<RoleOption> Roles,
    IReadOnlyList<BranchOption> Branches,
    UserId? CurrentUserId,
    bool CanManage);

/// <summary>The create / edit form. Validation rules live in the Application layer (one source of truth).</summary>
public sealed class UserFormModel
{
    [Display(Name = "Field.UserName")]
    public string? UserName { get; set; }

    [Display(Name = "Field.Email")]
    public string? Email { get; set; }

    [Display(Name = "Field.FullName")]
    public string? FullName { get; set; }

    [Display(Name = "Field.Role")]
    public Guid? RoleId { get; set; }

    [Display(Name = "Field.Branch")]
    public Guid? BranchId { get; set; }

    [Display(Name = "Field.Capacity")]
    public int? Capacity { get; set; } = StaffMember.DefaultCapacity;

    [Display(Name = "Field.RequireMfa")]
    public bool RequireMfa { get; set; }

    [BindNever]
    public IReadOnlyList<RoleOption> Roles { get; set; } = [];

    [BindNever]
    public IReadOnlyList<BranchOption> Branches { get; set; } = [];

    /// <summary>The user being edited (<c>null</c> on create).</summary>
    [BindNever]
    public UserDetails? Existing { get; set; }

    public RoleOption? SelectedRole => Roles.FirstOrDefault(r => r.Id == RoleId);

    /// <summary>Active branches, plus the current branch if it has been deactivated since.</summary>
    public IEnumerable<BranchOption> SelectableBranches =>
        Branches.Where(b => b.IsActive || b.Id == BranchId || b.Id == Existing?.BranchId?.Value);

    public static UserFormModel From(UserDetails details) => new()
    {
        UserName = details.UserName,
        Email = details.Email,
        FullName = details.FullName,
        RoleId = details.RoleId.Value,
        BranchId = details.BranchId?.Value,
        Capacity = details.Capacity,
        RequireMfa = details.RequireMfa,
        Existing = details,
    };

    public CreateUserCommand ToCreateCommand() => new()
    {
        UserName = UserName ?? string.Empty,
        Email = Email ?? string.Empty,
        FullName = FullName ?? string.Empty,
        RoleId = new RoleId(RoleId ?? Guid.Empty),
        BranchId = BranchId is { } branch ? new CreditFlow.Domain.Branches.BranchId(branch) : null,
        Capacity = Capacity ?? -1,
        RequireMfa = RequireMfa,
    };

    public UpdateUserCommand ToUpdateCommand(UserId id) => new()
    {
        Id = id,
        UserName = UserName ?? string.Empty,
        Email = Email ?? string.Empty,
        FullName = FullName ?? string.Empty,
        RoleId = new RoleId(RoleId ?? Guid.Empty),
        BranchId = BranchId is { } branch ? new CreditFlow.Domain.Branches.BranchId(branch) : null,
        Capacity = Capacity ?? -1,
    };
}

public sealed record UserDetailsViewModel(
    UserDetails User,
    IReadOnlyList<AuditLogListItem> RecentActivity,
    bool IsSelf,
    bool CanManage);

public sealed record RecentActivityViewModel(UserId UserId, IReadOnlyList<AuditLogListItem> Items);

/// <summary>The inline "Working today" switch of a user (list row or details page).</summary>
public sealed record AvailabilityToggleModel(UserId UserId, string FullName, bool IsAvailable, bool IsActive, bool CanManage);

/// <summary>User actions that ask for confirmation first.</summary>
public enum UserOperation
{
    Deactivate = 1,
    ForceSignOut = 2,
    ResetPassword = 3,
}

public sealed record ConfirmUserOperationModel(UserId UserId, string FullName, UserOperation Operation)
{
    public bool IsDangerous => Operation == UserOperation.Deactivate;
}

/// <summary>A temporary password, shown once (after creating a user or resetting the password).</summary>
public sealed record TemporaryPasswordModel(
    UserId UserId,
    string FullName,
    string UserName,
    string Password,
    DateTimeOffset ExpiresAt,
    bool IsNewUser);
