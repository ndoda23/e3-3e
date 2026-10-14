using CreditFlow.Domain.Applications;

namespace CreditFlow.Domain.Loans;

/// <summary>Persistence of the <see cref="Loan"/> aggregate (with its schedule and repayments).</summary>
public interface ILoanRepository
{
    Task<Loan?> GetAsync(LoanId id, CancellationToken cancellationToken);

    Task<Loan?> GetByApplicationAsync(LoanApplicationId applicationId, CancellationToken cancellationToken);

    void Add(Loan loan);
}
