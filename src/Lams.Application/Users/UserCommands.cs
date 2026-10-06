using System.Text.RegularExpressions;
using FluentValidation;
using Lams.Domain.Branches;
using Lams.Domain.Roles;
using Lams.Domain.Users;

namespace Lams.Application.Users;

public sealed record CreateUserCommand
{
    public required string UserName { get; init; }

    public required string Email { get; init; }

    public required string FullName { get; init; }

    public required RoleId RoleId { get; init; }

    public BranchId? BranchId { get; init; }

    public Segment? Segment { get; init; }

    public AuthorityLevel? AuthorityLevel { get; init; }

    public int Capacity { get; init; } = StaffMember.DefaultCapacity;

    /// <summary>Require MFA enrolment for this user regardless of the role setting.</summary>
    public bool RequireMfa { get; init; }
}

public sealed record UpdateUserCommand
{
    public required UserId Id { get; init; }

    public required string UserName { get; init; }

    public required string Email { get; init; }

    public required string FullName { get; init; }

    public required RoleId RoleId { get; init; }

    public BranchId? BranchId { get; init; }

    public Segment? Segment { get; init; }

    public AuthorityLevel? AuthorityLevel { get; init; }

    public int Capacity { get; init; } = StaffMember.DefaultCapacity;
}

/// <summary>A temporary password, shown once to the administrator and never stored in plain text.</summary>
public sealed record IssuedTemporaryPassword(UserId UserId, string TemporaryPassword, DateTimeOffset ExpiresAt);

public static class UserFieldRules
{
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 64;
    public const int EmailMaxLength = 256;

    public const string InvalidUserNameCode = "User.InvalidUserName";
    public const string InvalidEmailCode = "User.InvalidEmail";
}

internal static partial class UserRuleExtensions
{
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex UserNamePattern();

    public static void UserNameRules<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(name => name is not null
                          && name.Trim().Length is >= UserFieldRules.UserNameMinLength
                              and <= UserFieldRules.UserNameMaxLength
                          && UserNamePattern().IsMatch(name.Trim()))
            .WithErrorCode(UserFieldRules.InvalidUserNameCode)
            .WithMessage("A user name has 3 to 64 letters, digits, dots, hyphens or underscores.");

    public static void EmailRules<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(UserFieldRules.EmailMaxLength).EmailAddress()
            .WithErrorCode(UserFieldRules.InvalidEmailCode)
            .WithMessage("Enter a valid email address.");

    public static void FullNameRules<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(StaffMember.FullNameMaxLength)
            .WithErrorCode(UserErrors.InvalidFullName.Code)
            .WithMessage(UserErrors.InvalidFullName.Description);

    public static void CapacityRules<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(0, StaffMember.MaxCapacity)
            .WithErrorCode(UserErrors.InvalidCapacity.Code)
            .WithMessage(UserErrors.InvalidCapacity.Description);

    public static void RoleRules<T>(this IRuleBuilder<T, RoleId> rule) =>
        rule.Must(id => id.Value != Guid.Empty)
            .WithErrorCode(UserErrors.RoleRequired.Code)
            .WithMessage(UserErrors.RoleRequired.Description);
}

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(c => c.UserName).UserNameRules();
        RuleFor(c => c.Email).EmailRules();
        RuleFor(c => c.FullName).FullNameRules();
        RuleFor(c => c.RoleId).RoleRules();
        RuleFor(c => c.Capacity).CapacityRules();
    }
}

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(c => c.UserName).UserNameRules();
        RuleFor(c => c.Email).EmailRules();
        RuleFor(c => c.FullName).FullNameRules();
        RuleFor(c => c.RoleId).RoleRules();
        RuleFor(c => c.Capacity).CapacityRules();
    }
}
