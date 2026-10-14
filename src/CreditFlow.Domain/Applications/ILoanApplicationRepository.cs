using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Products;

namespace CreditFlow.Domain.Applications;

/// <summary>Persistence of the <see cref="LoanApplication"/> aggregate (with all its parts).</summary>
public interface ILoanApplicationRepository
{
    Task<LoanApplication?> GetAsync(LoanApplicationId id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the client already has another open application (before disbursement) for the product, as borrower.
    /// A filtered unique index enforces the same rule in the database.
    /// </summary>
    Task<bool> HasOpenApplicationAsync(
        ClientId borrowerId, LoanProductId productId, LoanApplicationId except, CancellationToken cancellationToken);

    /// <summary>The reference of the borrower's open application for the product, or <c>null</c> when there is none.</summary>
    Task<string?> FindOpenApplicationReferenceAsync(ClientId borrowerId, LoanProductId productId, CancellationToken cancellationToken);

    void Add(LoanApplication application);
}
