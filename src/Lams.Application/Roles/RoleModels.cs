using Lams.Domain.Roles;

namespace Lams.Application.Roles;

public sealed record UpdateRolePermissionsCommand(RoleId RoleId, IReadOnlyCollection<string> Permissions);

/// <summary>The permission matrix editor for one role.</summary>
public sealed record RolePermissionEditor(RoleDetails Role, IReadOnlyList<PermissionGroup> Groups);

public sealed record PermissionGroup(string ModuleKey, string ModuleName, IReadOnlyList<PermissionEditorItem> Items);

/// <param name="Code">Permission code.</param>
/// <param name="DisplayName">Name shown in the editor.</param>
/// <param name="Description">Description shown in the editor.</param>
/// <param name="IsGranted">Whether the role currently holds the permission.</param>
/// <param name="IsAssignable">False when segregation of duties forbids it for this role (checkbox disabled).</param>
/// <param name="IsLocked">True when the role must keep it (checked and disabled).</param>
public sealed record PermissionEditorItem(
    string Code,
    string DisplayName,
    string Description,
    bool IsGranted,
    bool IsAssignable,
    bool IsLocked);
