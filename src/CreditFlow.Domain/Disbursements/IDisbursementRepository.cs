using CreditFlow.Domain.Applications;

namespace CreditFlow.Domain.Disbursements;

/// <summary>Persistence of the <see cref="Disbursement"/> aggregate.</summary>
public interface IDisbursementRepository
{
    Task<Disbursement?> GetAsync(DisbursementId id, CancellationToken cancellationToken);

    Task<Disbursement?> GetByApplicationAsync(LoanApplicationId applicationId, CancellationToken cancellationToken);

    void Add(Disbursement disbursement);
}
