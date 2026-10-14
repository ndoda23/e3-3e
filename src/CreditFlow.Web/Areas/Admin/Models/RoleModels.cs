using CreditFlow.Application.Authorization;
using CreditFlow.Application.Roles;

namespace CreditFlow.Web.Areas.Admin.Models;

/// <summary>A permission row of the overview matrix: which roles hold it.</summary>
public sealed record MatrixRow(PermissionDefinition Permission, IReadOnlySet<Guid> GrantedTo);

public sealed record MatrixGroup(PermissionModule Module, IReadOnlyList<MatrixRow> Rows);

/// <summary>Admin › Roles: the role list and the read-only permission matrix (roles × permissions).</summary>
public sealed record RoleIndexViewModel(IReadOnlyList<RoleDetails> Roles, IReadOnlyList<MatrixGroup> Matrix)
{
    public static RoleIndexViewModel Create(IReadOnlyList<RoleDetails> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        // Business roles first, the administrator last (as in the sidebar: workspace, then administration).
        var ordered = roles.OrderBy(r => r.Profile.Kind).ThenBy(r => r.Profile.Dashboard).ToList();
        var matrix = PermissionCatalog.All
            .GroupBy(p => p.Module)
            .OrderBy(g => g.Key.Order)
            .Select(g => new MatrixGroup(
                g.Key,
                g.Select(p => new MatrixRow(
                        p,
                        ordered.Where(r => r.Permissions.Contains(p.Code)).Select(r => r.Id.Value).ToHashSet()))
                    .ToList()))
            .ToList();
        return new RoleIndexViewModel(ordered, matrix);
    }
}

/// <summary>The permission editor of one role; <see cref="Editor"/> reflects the posted selection after an error.</summary>
public sealed record RolePermissionsViewModel(RolePermissionEditor Editor)
{
    public RoleDetails Role => Editor.Role;

    public int GrantedCount => Editor.Groups.Sum(g => g.Items.Count(i => i.IsGranted));

    /// <summary>The editor with the checkboxes set to <paramref name="selected"/> (locked items stay granted).</summary>
    public static RolePermissionsViewModel WithSelection(RolePermissionEditor editor, IReadOnlyCollection<string> selected)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(selected);

        var set = selected.ToHashSet(StringComparer.Ordinal);
        return new RolePermissionsViewModel(editor with
        {
            Groups = editor.Groups
                .Select(g => g with
                {
                    Items = g.Items.Select(i => i with { IsGranted = i.IsLocked || set.Contains(i.Code) }).ToList(),
                })
                .ToList(),
        });
    }
}
