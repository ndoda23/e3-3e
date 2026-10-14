using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;

namespace CreditFlow.Application.Administration;

/// <summary>Checks an administration permission of the signed-in user (the web layer checks it first).</summary>
public sealed class AdminGuard(CurrentAccessProfile access)
{
    /// <summary><c>null</c> when allowed, otherwise the error to return.</summary>
    public async Task<Error?> DenyUnlessAsync(string permission, CancellationToken cancellationToken) =>
        await access.GetAsync(cancellationToken) switch
        {
            null => CommonErrors.NotAuthenticated,
            { } profile when !profile.HasPermission(permission) => CommonErrors.AccessDenied,
            _ => null,
        };
}

/// <summary>A new product with its first version.</summary>
public sealed record CreateProductCommand(string? Code, string? Name, ProductType Type, ProductTerms Terms);

/// <summary>
/// Admin › Products. A product's terms are versioned: a change publishes the next version and the newest version is the
/// one in force for new submissions; applications keep the snapshot of the version they were submitted with. Every
/// change is audited by the data audit (in the same transaction) and needs <c>admin.products.manage</c>.
/// </summary>
public sealed class ProductAdministrationService(
    AdminGuard guard, ILoanProductRepository products, ILendingAdminQueries queries, IUnitOfWork unitOfWork, TimeProvider clock)
{
    private const string Permission = Permissions.Admin.ProductsManage;

    public Task<IReadOnlyList<ProductAdminItem>> ListAsync(CancellationToken cancellationToken) =>
        queries.ListProductsAsync(cancellationToken);

    public Task<ProductAdminDetails?> GetAsync(LoanProductId id, CancellationToken cancellationToken) =>
        queries.GetProductAsync(id, cancellationToken);

    public async Task<Result<LoanProductId>> CreateAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await guard.DenyUnlessAsync(Permission, cancellationToken) is { } denied)
        {
            return Result.Failure<LoanProductId>(denied);
        }

        var created = LoanProduct.Create(command.Code, command.Name, command.Type, command.Terms, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<LoanProductId>(created.Error!);
        }

        if (await products.CodeExistsAsync(created.Value.Code, cancellationToken))
        {
            return Result.Failure<LoanProductId>(ProductErrors.DuplicateCode);
        }

        products.Add(created.Value);
        return await SaveAsync(created.Value.Id, cancellationToken);
    }

    /// <summary>Publishes <paramref name="terms"/> as the next version (refused when nothing changed).</summary>
    public Task<Result> PublishVersionAsync(LoanProductId id, ProductTerms terms, CancellationToken cancellationToken) =>
        ChangeAsync(id, product => product.PublishVersion(terms, clock.GetUtcNow()) is { IsFailure: true } failed
            ? Result.Failure(failed.Error!)
            : Result.Success(), cancellationToken);

    public Task<Result> RenameAsync(LoanProductId id, string? name, CancellationToken cancellationToken) =>
        ChangeAsync(id, product => product.Rename(name), cancellationToken);

    /// <summary>An inactive product takes no new applications; open applications continue on their snapshot.</summary>
    public Task<Result> SetActiveAsync(LoanProductId id, bool active, CancellationToken cancellationToken) =>
        ChangeAsync(id, product =>
        {
            if (active)
            {
                product.Activate();
            }
            else
            {
                product.Deactivate();
            }

            return Result.Success();
        }, cancellationToken);

    private async Task<Result> ChangeAsync(LoanProductId id, Func<LoanProduct, Result> change, CancellationToken cancellationToken)
    {
        if (await guard.DenyUnlessAsync(Permission, cancellationToken) is { } denied)
        {
            return Result.Failure(denied);
        }

        var product = await products.GetAsync(id, cancellationToken);
        if (product is null)
        {
            return Result.Failure(ProductErrors.NotFound);
        }

        var result = change(product);
        if (result.IsFailure)
        {
            return result;
        }

        var saved = await SaveAsync(id, cancellationToken);
        return saved.IsSuccess ? Result.Success() : Result.Failure(saved.Error!);
    }

    private async Task<Result<LoanProductId>> SaveAsync(LoanProductId id, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(id);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure<LoanProductId>(CommonErrors.ConcurrencyConflict);
        }
    }
}
