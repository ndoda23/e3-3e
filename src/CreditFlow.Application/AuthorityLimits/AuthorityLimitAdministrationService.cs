using FluentValidation;
using CreditFlow.Application.Common;
using CreditFlow.Application.Roles;
using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;

namespace CreditFlow.Application.AuthorityLimits;

/// <summary>Editing of both kinds of authority limits (credit decision and pricing exception).</summary>
public sealed class AuthorityLimitAdministrationService(
    IAuthorityLimitRepository limits,
    IRoleRepository roles,
    IUnitOfWork unitOfWork,
    IValidator<CreateCreditDecisionLimitCommand> createCreditValidator,
    IValidator<UpdateCreditDecisionLimitCommand> updateCreditValidator,
    IValidator<CreatePricingExceptionLimitCommand> createPricingValidator,
    IValidator<UpdatePricingExceptionLimitCommand> updatePricingValidator)
{
    public static readonly Error RoleMustBeBusinessRole = Error.Validation(
        "AuthorityLimit.RoleMustBeBusinessRole", "Authority limits apply only to business roles.", "RoleId");

    public static readonly Error AuthorityLevelNotAllowed = Error.Validation(
        "AuthorityLimit.AuthorityLevelNotAllowed",
        "This role has no authority levels; leave the level empty.",
        "AuthorityLevel");

    public static readonly Error AuthorityLevelRequired = Error.Validation(
        "AuthorityLimit.AuthorityLevelRequired",
        "This role has authority levels; choose the level the limit applies to.",
        "AuthorityLevel");

    public async Task<AuthorityLimitOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var all = await limits.ListAsync(cancellationToken);
        var roleList = await roles.ListAsync(cancellationToken);
        var roleById = roleList.ToDictionary(r => r.Id);

        var items = all
            .Where(l => roleById.ContainsKey(l.RoleId))
            .Select(l =>
            {
                var role = roleById[l.RoleId];
                return new AuthorityLimitItem(
                    l.Id, l.RoleId, role.Profile.Code.Value, role.DisplayName, l.AuthorityLevel, l.LimitType,
                    l.Currency, l.MaxAmount, l.MaxRateGapPp, l.MinCustomerRoaePct);
            })
            .ToList();

        return new AuthorityLimitOverview(
            items.Where(i => i.LimitType == LimitType.CreditDecision)
                .OrderBy(i => i.RoleCode, StringComparer.Ordinal)
                .ThenBy(i => i.Currency)
                .ThenBy(i => i.AuthorityLevel)
                .ToList(),
            items.Where(i => i.LimitType == LimitType.PricingException)
                .OrderBy(i => i.RoleCode, StringComparer.Ordinal)
                .ToList());
    }

    public async Task<Result<AuthorityLimitId>> CreateCreditDecisionAsync(
        CreateCreditDecisionLimitCommand command, CancellationToken cancellationToken)
    {
        var errors = await createCreditValidator.ValidateToErrorsAsync(command, cancellationToken);
        if (errors.Count > 0)
        {
            return Result.Failure<AuthorityLimitId>(Error.ValidationFailed(errors));
        }

        var role = await roles.GetProfileAsync(command.RoleId, cancellationToken);
        var roleCheck = CheckRole(role, command.AuthorityLevel is not null, requireLevelRule: true);
        if (roleCheck.IsFailure)
        {
            return Result.Failure<AuthorityLimitId>(roleCheck.Error!);
        }

        if (await limits.CreditDecisionLimitExistsAsync(
                command.RoleId, command.AuthorityLevel, command.Currency, cancellationToken))
        {
            return Result.Failure<AuthorityLimitId>(AuthorityLimitErrors.Duplicate);
        }

        var limit = AuthorityLimit.CreateCreditDecision(
            command.RoleId, command.AuthorityLevel, command.Currency, command.MaxAmount);
        if (limit.IsFailure)
        {
            return Result.Failure<AuthorityLimitId>(limit.Error!);
        }

        limits.Add(limit.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(limit.Value.Id);
    }

    public async Task<Result> UpdateCreditDecisionAsync(
        UpdateCreditDecisionLimitCommand command, CancellationToken cancellationToken)
    {
        var errors = await updateCreditValidator.ValidateToErrorsAsync(command, cancellationToken);
        if (errors.Count > 0)
        {
            return errors.ToResult();
        }

        var limit = await limits.GetAsync(command.Id, cancellationToken);
        if (limit is not { LimitType: LimitType.CreditDecision })
        {
            return Result.Failure(AuthorityLimitErrors.NotFound);
        }

        var changed = limit.ChangeCreditDecision(command.MaxAmount);
        if (changed.IsFailure)
        {
            return changed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<AuthorityLimitId>> CreatePricingExceptionAsync(
        CreatePricingExceptionLimitCommand command, CancellationToken cancellationToken)
    {
        var errors = await createPricingValidator.ValidateToErrorsAsync(command, cancellationToken);
        if (errors.Count > 0)
        {
            return Result.Failure<AuthorityLimitId>(Error.ValidationFailed(errors));
        }

        var role = await roles.GetProfileAsync(command.RoleId, cancellationToken);
        var roleCheck = CheckRole(role, hasLevel: false, requireLevelRule: false);
        if (roleCheck.IsFailure)
        {
            return Result.Failure<AuthorityLimitId>(roleCheck.Error!);
        }

        if (await limits.PricingExceptionLimitExistsAsync(command.RoleId, cancellationToken))
        {
            return Result.Failure<AuthorityLimitId>(AuthorityLimitErrors.Duplicate);
        }

        var limit = AuthorityLimit.CreatePricingException(
            command.RoleId, command.MaxRateGapPp, command.MinCustomerRoaePct);
        if (limit.IsFailure)
        {
            return Result.Failure<AuthorityLimitId>(limit.Error!);
        }

        limits.Add(limit.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(limit.Value.Id);
    }

    public async Task<Result> UpdatePricingExceptionAsync(
        UpdatePricingExceptionLimitCommand command, CancellationToken cancellationToken)
    {
        var errors = await updatePricingValidator.ValidateToErrorsAsync(command, cancellationToken);
        if (errors.Count > 0)
        {
            return errors.ToResult();
        }

        var limit = await limits.GetAsync(command.Id, cancellationToken);
        if (limit is not { LimitType: LimitType.PricingException })
        {
            return Result.Failure(AuthorityLimitErrors.NotFound);
        }

        var changed = limit.ChangePricingException(command.MaxRateGapPp, command.MinCustomerRoaePct);
        if (changed.IsFailure)
        {
            return changed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(AuthorityLimitId id, CancellationToken cancellationToken)
    {
        var limit = await limits.GetAsync(id, cancellationToken);
        if (limit is null)
        {
            return Result.Failure(AuthorityLimitErrors.NotFound);
        }

        limits.Remove(limit);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Result CheckRole(RoleProfile? role, bool hasLevel, bool requireLevelRule)
    {
        if (role is null)
        {
            return Result.Failure(RoleAdministrationErrors.NotFound);
        }

        if (role.IsAdministrative)
        {
            return Result.Failure(RoleMustBeBusinessRole);
        }

        if (!requireLevelRule)
        {
            return Result.Success();
        }

        return role.AuthorityLevelRule switch
        {
            AttributeRule.NotAllowed when hasLevel => Result.Failure(AuthorityLevelNotAllowed),
            AttributeRule.Required when !hasLevel => Result.Failure(AuthorityLevelRequired),
            _ => Result.Success(),
        };
    }
}
