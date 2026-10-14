namespace CreditFlow.Domain.Roles;

/// <summary>
/// Persistence of a role: its access-relevant profile, its <see cref="PermissionSet"/> and its MFA requirement.
/// Changes are saved with the unit of work.
/// </summary>
public interface IRoleRepository
{
    Task<RoleProfile?> GetProfileAsync(RoleId roleId, CancellationToken cancellationToken);

    /// <summary>Replaces the role's permissions.</summary>
    Task SetPermissionsAsync(RoleId roleId, PermissionSet permissions, CancellationToken cancellationToken);

    Task SetRequireMfaAsync(RoleId roleId, bool requireMfa, CancellationToken cancellationToken);
}
