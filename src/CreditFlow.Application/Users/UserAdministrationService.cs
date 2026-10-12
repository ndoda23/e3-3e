using FluentValidation;
using CreditFlow.Application.Auditing;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Branches;
using CreditFlow.Application.Common;
using CreditFlow.Application.Roles;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using Microsoft.Extensions.Options;

namespace CreditFlow.Application.Users;

/// <summary>
/// User administration. Business data is edited on <see cref="StaffMember"/>; credentials, lockout, MFA and sessions
/// go through <see cref="IIdentityService"/>. Every staff-member edit is saved through <see cref="StaffAccessChangeStep"/>.
/// </summary>
public sealed class UserAdministrationService(
    IStaffRepository staff,
    IRoleRepository roles,
    IBranchRepository branches,
    IIdentityService identity,
    IUserDirectory directory,
    ITemporaryPasswordGenerator passwordGenerator,
    StaffAccessChangeStep accessChangeStep,
    IAccessCacheInvalidator accessCache,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<SecurityOptions> securityOptions,
    IValidator<CreateUserCommand> createValidator,
    IValidator<UpdateUserCommand> updateValidator)
{
    private const string UserEntity = "User";

    public Task<PagedResult<UserListItem>> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return directory.SearchAsync(query.Normalize(), cancellationToken);
    }

    public Task<UserDetails?> GetDetailsAsync(UserId id, CancellationToken cancellationToken) =>
        directory.GetDetailsAsync(id, cancellationToken);

    public async Task<Result<IssuedTemporaryPassword>> CreateAsync(
        CreateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Every field error at once: the role-driven attributes are checked even when other fields are invalid.
        var errors = await createValidator.ValidateToErrorsAsync(command, cancellationToken);
        var role = await CheckAssignmentAsync(
            errors, command.RoleId, command.BranchId, command.Segment, command.AuthorityLevel, null, cancellationToken);
        if (errors.Count == 0)
        {
            await CheckUniquenessAsync(errors, command.UserName, command.Email, null, cancellationToken);
        }

        if (errors.Count > 0)
        {
            return Result.Failure<IssuedTemporaryPassword>(Error.ValidationFailed(errors));
        }

        var created = StaffMember.Create(
            UserId.New(), command.FullName, role!, command.BranchId, command.Segment, command.AuthorityLevel,
            command.Capacity);
        if (created.IsFailure)
        {
            return Result.Failure<IssuedTemporaryPassword>(created.Error!);
        }

        var member = created.Value;
        var issued = IssueTemporaryPassword(member.Id);

        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // The account is created first: the staff member shares (and references) its id.
                var account = await identity.CreateAccountAsync(
                    new NewAccount(
                        member.Id,
                        command.UserName.Trim(),
                        command.Email.Trim(),
                        issued.TemporaryPassword,
                        issued.ExpiresAt,
                        command.RequireMfa),
                    token);
                if (account.IsFailure)
                {
                    return account;
                }

                staff.Add(member);
                await unitOfWork.SaveChangesAsync(token);
                return Result.Success();
            },
            cancellationToken);

        return result.IsSuccess ? Result.Success(issued) : Result.Failure<IssuedTemporaryPassword>(result.Error!);
    }

    public async Task<Result> UpdateAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        var member = await staff.GetAsync(command.Id, cancellationToken);
        if (member is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        var errors = await updateValidator.ValidateToErrorsAsync(command, cancellationToken);
        var newRole = await CheckAssignmentAsync(
            errors, command.RoleId, command.BranchId, command.Segment, command.AuthorityLevel, member.BranchId,
            cancellationToken);
        if (errors.Count == 0)
        {
            await CheckUniquenessAsync(errors, command.UserName, command.Email, member.Id, cancellationToken);
        }

        if (errors.Count > 0)
        {
            return Result.Failure(Error.ValidationFailed(errors));
        }

        if (newRole!.Id != member.RoleId)
        {
            var currentRole = await roles.GetProfileAsync(member.RoleId, cancellationToken);
            var continuity = AdminContinuityPolicy.EnsureCanChangeRole(
                actor,
                member.Id,
                targetIsActiveAdministrator: member.IsActive && currentRole?.IsAdministrative == true,
                newRoleIsAdministrative: newRole.IsAdministrative,
                activeAdministratorCount: await staff.CountActiveAdministratorsAsync(cancellationToken));
            if (continuity.IsFailure)
            {
                return continuity;
            }
        }

        var before = member.Access;
        var edit = member.Rename(command.FullName);
        if (edit.IsSuccess)
        {
            edit = member.ChangeCapacity(command.Capacity);
        }

        if (edit.IsSuccess)
        {
            edit = member.AssignRole(newRole, command.BranchId, command.Segment, command.AuthorityLevel);
        }

        if (edit.IsFailure)
        {
            return edit;
        }

        return await accessChangeStep.SaveAsync(
            member,
            before,
            token => identity.UpdateAccountAsync(member.Id, command.UserName.Trim(), command.Email.Trim(), token),
            cancellationToken);
    }

    public async Task<Result> DeactivateAsync(UserId id, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        var member = await staff.GetAsync(id, cancellationToken);
        if (member is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        if (!member.IsActive)
        {
            return Result.Success();
        }

        var role = await roles.GetProfileAsync(member.RoleId, cancellationToken);
        var continuity = AdminContinuityPolicy.EnsureCanDeactivate(
            actor,
            member.Id,
            targetIsActiveAdministrator: role?.IsAdministrative == true,
            activeAdministratorCount: await staff.CountActiveAdministratorsAsync(cancellationToken));
        if (continuity.IsFailure)
        {
            return continuity;
        }

        var before = member.Access;
        member.Deactivate();
        return await accessChangeStep.SaveAsync(
            member, before, token => WriteAuditAsync(AuditActions.UserDeactivated, member.Id, token), cancellationToken);
    }

    public async Task<Result> ActivateAsync(UserId id, CancellationToken cancellationToken)
    {
        var member = await staff.GetAsync(id, cancellationToken);
        if (member is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        if (member.IsActive)
        {
            return Result.Success();
        }

        var before = member.Access;
        member.Activate();
        return await accessChangeStep.SaveAsync(
            member, before, token => WriteAuditAsync(AuditActions.UserActivated, member.Id, token), cancellationToken);
    }

    public async Task<Result> SetAvailabilityAsync(UserId id, bool isAvailable, CancellationToken cancellationToken)
    {
        var member = await staff.GetAsync(id, cancellationToken);
        if (member is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        var before = member.Access;
        var changed = member.SetAvailability(isAvailable);
        return changed.IsFailure
            ? changed
            : await accessChangeStep.SaveAsync(member, before, null, cancellationToken);
    }

    public async Task<Result> UnlockAsync(UserId id, CancellationToken cancellationToken)
    {
        if (await staff.GetAsync(id, cancellationToken) is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var unlocked = await identity.UnlockAsync(id, token);
                return unlocked.IsFailure ? unlocked : await WriteAuditAsync(AuditActions.UserUnlocked, id, token);
            },
            cancellationToken);
    }

    /// <summary>Issues a new temporary password (shown once) and ends the user's sessions.</summary>
    public async Task<Result<IssuedTemporaryPassword>> ResetPasswordAsync(
        UserId id, CancellationToken cancellationToken)
    {
        if (await staff.GetAsync(id, cancellationToken) is null)
        {
            return Result.Failure<IssuedTemporaryPassword>(UserErrors.NotFound);
        }

        var issued = IssueTemporaryPassword(id);
        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var reset = await identity.SetTemporaryPasswordAsync(
                    id, issued.TemporaryPassword, issued.ExpiresAt, token);
                return reset.IsFailure ? reset : await WriteAuditAsync(AuditActions.PasswordReset, id, token);
            },
            cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<IssuedTemporaryPassword>(result.Error!);
        }

        // The user must change the new temporary password: refresh their cached account state.
        await accessCache.InvalidateUserAsync(id, cancellationToken);
        return Result.Success(issued);
    }

    /// <summary>Ends all of the user's sessions (effective within the security stamp validation interval).</summary>
    public async Task<Result> ForceSignOutAsync(UserId id, CancellationToken cancellationToken)
    {
        if (await staff.GetAsync(id, cancellationToken) is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var invalidated = await identity.InvalidateSessionsAsync(id, token);
                return invalidated.IsFailure
                    ? invalidated
                    : await WriteAuditAsync(AuditActions.ForcedSignOut, id, token);
            },
            cancellationToken);
    }

    public async Task<Result> SetRequireMfaAsync(UserId id, bool requireMfa, CancellationToken cancellationToken)
    {
        if (await staff.GetAsync(id, cancellationToken) is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var set = await identity.SetRequireMfaAsync(id, requireMfa, token);
                return set.IsFailure
                    ? set
                    : await WriteAuditAsync(AuditActions.MfaRequirementChanged, id, token, requireMfa.ToString());
            },
            cancellationToken);

        if (result.IsSuccess)
        {
            await accessCache.InvalidateUserAsync(id, cancellationToken);
        }

        return result;
    }

    private IssuedTemporaryPassword IssueTemporaryPassword(UserId id) =>
        new(
            id,
            passwordGenerator.Generate(),
            TemporaryPasswordPolicy.ExpiresAt(clock.GetUtcNow(), securityOptions.Value.TemporaryPasswordLifetime));

    /// <summary>Checks the role and the attributes it governs; returns the role profile when it exists.</summary>
    private async Task<RoleProfile?> CheckAssignmentAsync(
        List<Error> errors,
        RoleId roleId,
        BranchId? branchId,
        Segment? segment,
        AuthorityLevel? authorityLevel,
        BranchId? currentBranchId,
        CancellationToken cancellationToken)
    {
        if (roleId.Value == Guid.Empty)
        {
            return null; // already reported by the validator (RoleRequired)
        }

        var role = await roles.GetProfileAsync(roleId, cancellationToken);
        if (role is null)
        {
            errors.Add(UserAdministrationErrors.RoleNotFound);
            return null;
        }

        errors.AddRange(StaffMember.ValidateAssignment(role, branchId, segment, authorityLevel));

        // A new branch must exist and be active; keeping a branch that was deactivated later is allowed.
        if (branchId is { } id && role.BranchRule != AttributeRule.NotAllowed && id != currentBranchId)
        {
            var branch = await branches.GetAsync(id, cancellationToken);
            if (branch is null)
            {
                errors.Add(UserAdministrationErrors.BranchNotFound);
            }
            else if (!branch.IsActive)
            {
                errors.Add(BranchErrors.Inactive);
            }
        }

        return role;
    }

    private async Task CheckUniquenessAsync(
        List<Error> errors, string userName, string email, UserId? exceptUserId, CancellationToken cancellationToken)
    {
        if (await identity.IsUserNameTakenAsync(userName.Trim(), exceptUserId, cancellationToken))
        {
            errors.Add(UserAdministrationErrors.UserNameTaken);
        }

        if (await identity.IsEmailTakenAsync(email.Trim(), exceptUserId, cancellationToken))
        {
            errors.Add(UserAdministrationErrors.EmailTaken);
        }
    }

    private async Task<Result> WriteAuditAsync(
        string action, UserId subject, CancellationToken cancellationToken, string? detail = null)
    {
        await audit.WriteAsync(
            new AuditEvent(action)
            {
                EntityType = UserEntity,
                EntityId = subject.ToString(),
                AfterJson = detail is null ? null : System.Text.Json.JsonSerializer.Serialize(detail),
            },
            cancellationToken);
        return Result.Success();
    }
}
