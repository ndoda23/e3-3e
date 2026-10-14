using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Approval;

/// <summary>
/// Who decides by amount (GEL equivalent): up to <see cref="UnderwriterMaxGel"/> an Underwriter, up to
/// <see cref="SeniorUnderwriterMaxGel"/> a Senior Underwriter, above it the Credit Committee, where
/// <see cref="RequiredVotes"/> matching votes decide. Configurable by the Admin (a single row).
/// </summary>
public sealed class ApprovalThresholds
{
    public const int SingletonId = 1;

    private ApprovalThresholds()
    {
    }

    public int Id { get; private set; } = SingletonId;

    public decimal UnderwriterMaxGel { get; private set; }

    public decimal SeniorUnderwriterMaxGel { get; private set; }

    public int RequiredVotes { get; private set; }

    /// <summary>The task's defaults: 20,000 / 100,000 GEL, 2 votes of 3.</summary>
    public static ApprovalThresholds Default() => Create(20_000m, 100_000m, 2).Value;

    public static Result<ApprovalThresholds> Create(decimal underwriterMaxGel, decimal seniorUnderwriterMaxGel, int requiredVotes)
    {
        var thresholds = new ApprovalThresholds();
        var changed = thresholds.Change(underwriterMaxGel, seniorUnderwriterMaxGel, requiredVotes);
        return changed.IsSuccess ? Result.Success(thresholds) : Result.Failure<ApprovalThresholds>(changed.Error!);
    }

    public Result Change(decimal underwriterMaxGel, decimal seniorUnderwriterMaxGel, int requiredVotes)
    {
        var errors = new List<Error>();
        if (underwriterMaxGel <= 0m || seniorUnderwriterMaxGel <= underwriterMaxGel)
        {
            errors.Add(ApprovalErrors.InvalidThresholds);
        }

        if (requiredVotes is < 1 or > 9)
        {
            errors.Add(ApprovalErrors.InvalidRequiredVotes);
        }

        if (errors.Count > 0)
        {
            return Result.Failure(Error.ValidationFailed(errors));
        }

        UnderwriterMaxGel = underwriterMaxGel;
        SeniorUnderwriterMaxGel = seniorUnderwriterMaxGel;
        RequiredVotes = requiredVotes;
        return Result.Success();
    }

    /// <summary>The tier that decides an application of this GEL amount (limits are inclusive).</summary>
    public ApprovalTier TierFor(decimal amountGel) =>
        amountGel <= UnderwriterMaxGel ? ApprovalTier.Underwriter
        : amountGel <= SeniorUnderwriterMaxGel ? ApprovalTier.SeniorUnderwriter
        : ApprovalTier.Committee;
}

public static class ApprovalErrors
{
    public static readonly Error InvalidThresholds = Error.Validation(
        "Approval.InvalidThresholds",
        "The Underwriter threshold must be above zero and below the Senior Underwriter threshold.",
        "UnderwriterMaxGel");

    public static readonly Error InvalidRequiredVotes = Error.Validation(
        "Approval.InvalidRequiredVotes", "The committee needs 1 to 9 matching votes.", "RequiredVotes");
}
