namespace CreditFlow.Domain.Products;

/// <summary>Persistence of the <see cref="LoanProduct"/> aggregate (with all its versions).</summary>
public interface ILoanProductRepository
{
    Task<LoanProduct?> GetAsync(LoanProductId id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    void Add(LoanProduct product);
}
