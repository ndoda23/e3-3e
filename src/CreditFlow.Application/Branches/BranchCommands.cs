using System.Text.RegularExpressions;
using FluentValidation;
using CreditFlow.Domain.Branches;

namespace CreditFlow.Application.Branches;

public sealed record CreateBranchCommand(string Code, string Name, string City);

public sealed record UpdateBranchCommand(BranchId Id, string Name, string City);

public sealed partial class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    [GeneratedRegex("^[A-Za-z0-9-]{2,10}$")]
    private static partial Regex CodePattern();

    public CreateBranchCommandValidator()
    {
        RuleFor(c => c.Code)
            .Must(code => code is not null && CodePattern().IsMatch(code.Trim()))
            .WithErrorCode(BranchErrors.InvalidCode.Code).WithMessage(BranchErrors.InvalidCode.Description);

        RuleFor(c => c.Name)
            .NotEmpty().MaximumLength(Branch.NameMaxLength)
            .WithErrorCode(BranchErrors.InvalidName.Code).WithMessage(BranchErrors.InvalidName.Description);

        RuleFor(c => c.City)
            .NotEmpty().MaximumLength(Branch.CityMaxLength)
            .WithErrorCode(BranchErrors.InvalidCity.Code).WithMessage(BranchErrors.InvalidCity.Description);
    }
}

public sealed class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().MaximumLength(Branch.NameMaxLength)
            .WithErrorCode(BranchErrors.InvalidName.Code).WithMessage(BranchErrors.InvalidName.Description);

        RuleFor(c => c.City)
            .NotEmpty().MaximumLength(Branch.CityMaxLength)
            .WithErrorCode(BranchErrors.InvalidCity.Code).WithMessage(BranchErrors.InvalidCity.Description);
    }
}
