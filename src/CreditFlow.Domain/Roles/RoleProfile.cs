namespace CreditFlow.Domain.Roles;

/// <summary>
/// The access-relevant definition of a role, stored as data so that code never branches on role names.
/// </summary>
public sealed record RoleProfile
{
    public required RoleId Id { get; init; }

    public required RoleCode Code { get; init; }

    public required RoleKind Kind { get; init; }

    public required DataScope DataScope { get; init; }

    public required DashboardKind Dashboard { get; init; }

    public required AttributeRule BranchRule { get; init; }

    /// <summary>Administrative roles hold only <c>admin.*</c> permissions and see no business data.</summary>
    public bool IsAdministrative => Kind == RoleKind.Administrative;
}
