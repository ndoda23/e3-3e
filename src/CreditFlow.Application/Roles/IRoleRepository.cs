using CreditFlow.Domain.Roles;

namespace CreditFlow.Application.Roles;

/// <summary>A role as shown and edited in administration.</summary>
public sealed record RoleDetails
{
    public required RoleProfile Profile { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    /// <summary>System roles cannot be deleted or renamed (all seeded roles are system roles).</summary>
    public required bool IsSystem { get; init; }

    /// <summary>Users of this role must enrol in MFA.</summary>
    public required bool RequireMfa { get; init; }

    public required int UserCount { get; init; }

    public required IReadOnlyCollection<string> Permissions { get; init; }

    public RoleId Id => Profile.Id;
}

public interface IRoleRepository
{
    Task<RoleProfile?> GetProfileAsync(RoleId roleId, CancellationToken cancellationToken);

    Task<RoleDetails?> GetDetailsAsync(RoleId roleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleDetails>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the role's permissions (saved with the unit of work).</summary>
    Task SetPermissionsAsync(RoleId roleId, PermissionSet permissions, CancellationToken cancellationToken);

    Task SetRequireMfaAsync(RoleId roleId, bool requireMfa, CancellationToken cancellationToken);
}
