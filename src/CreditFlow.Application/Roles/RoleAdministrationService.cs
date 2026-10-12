using System.Text.Json;
using CreditFlow.Application.Auditing;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;

namespace CreditFlow.Application.Roles;

/// <summary>Role administration: list roles, edit role permissions (permission matrix) and MFA requirement.</summary>
public sealed class RoleAdministrationService(
    IRoleRepository roles,
    IAccessCacheInvalidator accessCache,
    IAuditWriter audit,
    IUnitOfWork unitOfWork)
{
    public Task<IReadOnlyList<RoleDetails>> ListAsync(CancellationToken cancellationToken) =>
        roles.ListAsync(cancellationToken);

    public Task<RoleProfile?> GetProfileAsync(RoleId roleId, CancellationToken cancellationToken) =>
        roles.GetProfileAsync(roleId, cancellationToken);

    public async Task<Result<RolePermissionEditor>> GetPermissionEditorAsync(
        RoleId roleId, CancellationToken cancellationToken)
    {
        var role = await roles.GetDetailsAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RolePermissionEditor>(RoleAdministrationErrors.NotFound);
        }

        var granted = role.Permissions.ToHashSet(StringComparer.Ordinal);
        var isAdministrative = role.Profile.IsAdministrative;

        var groups = PermissionCatalog.All
            .GroupBy(p => p.Module)
            .OrderBy(g => g.Key.Order)
            .Select(g => new PermissionGroup(
                g.Key.Key,
                g.Key.DisplayName,
                g.Select(p => new PermissionEditorItem(
                        p.Code,
                        p.DisplayName,
                        p.Description,
                        IsGranted: granted.Contains(p.Code),
                        IsAssignable: p.IsAdministrative == isAdministrative,
                        IsLocked: isAdministrative && RoleRules.RequiredAdministrativePermissions.Contains(p.Code)))
                    .ToList()))
            .ToList();

        return Result.Success(new RolePermissionEditor(role, groups));
    }

    public async Task<Result> UpdatePermissionsAsync(
        UpdateRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var role = await roles.GetDetailsAsync(command.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure(RoleAdministrationErrors.NotFound);
        }

        var requested = command.Permissions.Select(p => p.Trim()).Distinct(StringComparer.Ordinal).ToList();

        var unknown = requested.FirstOrDefault(code => !PermissionCatalog.Contains(code));
        if (unknown is not null)
        {
            return Result.Failure(RoleAdministrationErrors.UnknownPermission(unknown));
        }

        var permissionSet = PermissionSet.Create(role.Profile.IsAdministrative, requested);
        if (permissionSet.IsFailure)
        {
            return Result.Failure(permissionSet.Error!);
        }

        if (role.Profile.IsAdministrative)
        {
            var missing = RoleRules.RequiredAdministrativePermissions.FirstOrDefault(p => !requested.Contains(p));
            if (missing is not null)
            {
                return Result.Failure(RoleAdministrationErrors.MustKeepAdministrationPermission(missing));
            }
        }

        var before = role.Permissions.Order(StringComparer.Ordinal).ToList();
        var after = requested.Order(StringComparer.Ordinal).ToList();
        if (before.SequenceEqual(after))
        {
            return Result.Success();
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await roles.SetPermissionsAsync(role.Id, permissionSet.Value, token);
                await unitOfWork.SaveChangesAsync(token);
                await audit.WriteAsync(
                    new AuditEvent(AuditActions.RolePermissionsChanged)
                    {
                        EntityType = "Role",
                        EntityId = role.Profile.Code.Value,
                        BeforeJson = JsonSerializer.Serialize(before),
                        AfterJson = JsonSerializer.Serialize(after),
                    },
                    token);
                return Result.Success();
            },
            cancellationToken);

        // After commit: every user of the role re-resolves their permissions on the next request.
        if (result.IsSuccess)
        {
            await accessCache.InvalidateRoleAsync(role.Id, cancellationToken);
        }

        return result;
    }

    public async Task<Result> SetRequireMfaAsync(RoleId roleId, bool requireMfa, CancellationToken cancellationToken)
    {
        var role = await roles.GetDetailsAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure(RoleAdministrationErrors.NotFound);
        }

        if (role.RequireMfa == requireMfa)
        {
            return Result.Success();
        }

        await roles.SetRequireMfaAsync(roleId, requireMfa, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await accessCache.InvalidateRoleAsync(roleId, cancellationToken);
        return Result.Success();
    }
}
