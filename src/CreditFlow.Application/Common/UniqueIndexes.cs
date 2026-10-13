using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Common;

/// <summary>
/// The database's unique indexes that a concurrent request can hit after the service's own check passed, and the
/// business error each one means. A violation is never shown as a 500.
/// </summary>
public static class UniqueIndexes
{
    public const string OpenApplicationPerBorrowerAndProduct = "UX_LoanApplications_OpenPerBorrowerAndProduct";
    public const string OneDecisionPerPerson = "UX_ApplicationDecisions_OnePerPerson";
    public const string ClientPersonalNumber = "IX_Clients_PersonalNumber";
    public const string ApplicationContractNumber = "IX_LoanApplications_ContractNumber";
    public const string PayoutPerApplication = "IX_Disbursements_ApplicationId";
    public const string PayoutIdempotencyKey = "IX_Disbursements_IdempotencyKey";
    public const string LoanPerApplication = "IX_Loans_ApplicationId";
    public const string LoanContractNumber = "IX_Loans_ContractNumber";

    /// <summary>
    /// The business error for a violation of <paramref name="indexName"/>. Indexes that only a race can hit (contract
    /// number, one payout or loan per application) are "someone else did this at the same time": reload and retry.
    /// </summary>
    public static Error ErrorFor(string? indexName) => indexName switch
    {
        OpenApplicationPerBorrowerAndProduct => ApplicationErrors.OpenApplicationExists,
        OneDecisionPerPerson => ApplicationErrors.AlreadyDecided,
        ClientPersonalNumber => ClientErrors.DuplicatePersonalNumber,
        _ => CommonErrors.ConcurrencyConflict,
    };
}
