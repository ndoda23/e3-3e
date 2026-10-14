using System.Globalization;
using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Disbursements;

/// <summary>
/// Step 7: the operator marks the contract as signed (the contract number is assigned here) and the conditions as met.
/// </summary>
public sealed class ContractService(
    CurrentAccessProfile access,
    ILoanApplicationRepository applications,
    IContractNumberGenerator contractNumbers,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary><c>LN-2026-000123</c>: the bank's year (Tbilisi) and the gapless number of that year.</summary>
    public static string Format(int year, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"LN-{year}-{number:000000}");

    /// <summary>
    /// The client signed: the next contract number is taken in the same transaction as the status change, so when the
    /// signing fails (offer expired, wrong status, a concurrent change) the number is rolled back and reused.
    /// </summary>
    public Task<Result> SignAsync(LoanApplicationId id, CancellationToken cancellationToken) =>
        ApplicationChange.RunAsync(access, applications, unitOfWork, clock, id, Permissions.Contracts.Manage,
            async (application, profile, now) =>
            {
                if (application.Status is not (ApplicationStatus.Approved or ApplicationStatus.ConditionallyApproved))
                {
                    return Result.Failure(ApplicationErrors.WrongStatus(application.Status));
                }

                var year = BankTime.DateOf(now).Year;
                var number = await contractNumbers.NextAsync(year, cancellationToken);
                return application.SignContract(profile.UserId, Format(year, number), now);
            },
            cancellationToken);

    public Task<Result> MarkConditionMetAsync(LoanApplicationId id, ApplicationLineId conditionId, CancellationToken cancellationToken) =>
        ApplicationChange.RunAsync(access, applications, unitOfWork, clock, id, Permissions.Contracts.Manage,
            (application, profile, now) => Task.FromResult(application.MarkConditionMet(profile.UserId, conditionId, now)),
            cancellationToken);
}
