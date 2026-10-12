using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Loans;
using CreditFlow.Domain.Products;

namespace CreditFlow.Application.Disbursements;

/// <summary>One installment of the loan page.</summary>
public sealed record InstallmentView(
    int Number, DateOnly DueDate, Money Principal, Money Interest, Money Fee, Money Total, bool IsPaid, DateOnly? PaidOn);

/// <summary>One recorded repayment of the loan page.</summary>
public sealed record RepaymentView(
    RepaymentKind Kind, DateOnly PaidOn, Money Principal, Money Interest, Money Fee, Money Penalty, Money Total,
    string RecordedBy);

/// <summary>Everything the loan page shows.</summary>
public sealed record LoanDetails
{
    public required LoanId Id { get; init; }

    public required LoanApplicationId ApplicationId { get; init; }

    public required string ApplicationReference { get; init; }

    public required string ContractNumber { get; init; }

    public required string BorrowerName { get; init; }

    public required string ProductName { get; init; }

    public required Money Principal { get; init; }

    public required decimal AnnualRate { get; init; }

    public required decimal EffectiveAnnualRate { get; init; }

    public required ScheduleType ScheduleType { get; init; }

    public required DateOnly DisbursedOn { get; init; }

    public required LoanStatus Status { get; init; }

    public required Money OutstandingPrincipal { get; init; }

    public required decimal EarlyRepaymentPenaltyPercent { get; init; }

    public required IReadOnlyList<InstallmentView> Installments { get; init; }

    public required IReadOnlyList<RepaymentView> Repayments { get; init; }
}

/// <summary>Filters and paging of the loan list (applied in SQL).</summary>
public sealed record LoanSearch
{
    /// <summary>Contract number, borrower name or personal number.</summary>
    public string? Text { get; init; }

    public LoanStatus? Status { get; init; }

    public int Page { get; init; } = 1;

    public LoanSearch Normalize() => this with
    {
        Text = string.IsNullOrWhiteSpace(Text) ? null : Text.Trim(),
        Page = Paging.NormalizePage(Page),
    };
}

/// <summary>One row of the loan list.</summary>
public sealed record LoanListItem(
    LoanId Id, string ContractNumber, string BorrowerName, string BorrowerPersonalNumber, string ProductName, Money Principal,
    Money OutstandingPrincipal, LoanStatus Status, DateOnly DisbursedOn, DateOnly? NextDueDate);

/// <summary>Read side of loans. Every method applies the caller's <see cref="ApplicationAccessFilter"/>.</summary>
public interface ILoanQueries
{
    Task<PagedResult<LoanListItem>> SearchAsync(LoanSearch search, ApplicationAccessFilter access, CancellationToken cancellationToken);

    /// <summary>The loan, or <c>null</c> when it does not exist or its application is not visible.</summary>
    Task<LoanDetails?> GetAsync(LoanId id, ApplicationAccessFilter access, CancellationToken cancellationToken);

    /// <summary>The loan booked for a visible application, if any.</summary>
    Task<LoanId?> FindByApplicationAsync(LoanApplicationId applicationId, ApplicationAccessFilter access, CancellationToken cancellationToken);
}

/// <summary>
/// Loans after disbursement: the loan page and the repayments an operator records (there is no collection system):
/// the next installment, or an early repayment (partial: the remaining installments are recalculated; full: the loan
/// and its application are closed). A loan is visible exactly when its application is.
/// </summary>
public sealed class LoanService(
    CurrentAccessProfile access,
    ILoanRepository loans,
    ILoanApplicationRepository applications,
    ILoanQueries queries,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<PagedResult<LoanListItem>> SearchAsync(LoanSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        return await access.GetAsync(cancellationToken) is { } profile && profile.HasPermission(Permissions.Loans.View)
            ? await queries.SearchAsync(search.Normalize(), ApplicationAccessFilter.For(profile), cancellationToken)
            : new PagedResult<LoanListItem>([], 0, 1, Paging.DefaultPageSize);
    }

    public async Task<LoanDetails?> GetAsync(LoanId id, CancellationToken cancellationToken) =>
        await access.GetAsync(cancellationToken) is { } profile && profile.HasPermission(Permissions.Loans.View)
            ? await queries.GetAsync(id, ApplicationAccessFilter.For(profile), cancellationToken)
            : null;

    public async Task<LoanId?> FindByApplicationAsync(LoanApplicationId applicationId, CancellationToken cancellationToken) =>
        await access.GetAsync(cancellationToken) is { } profile && profile.HasPermission(Permissions.Loans.View)
            ? await queries.FindByApplicationAsync(applicationId, ApplicationAccessFilter.For(profile), cancellationToken)
            : null;

    /// <summary>What an early repayment of <paramref name="principal"/> (everything when <c>null</c>) costs today.</summary>
    public async Task<Result<EarlyRepaymentQuote>> QuoteEarlyRepaymentAsync(
        LoanId id, decimal? principal, CancellationToken cancellationToken)
    {
        var loan = await LoadVisibleAsync(id, Permissions.Loans.View, cancellationToken);
        return loan.IsFailure
            ? Result.Failure<EarlyRepaymentQuote>(loan.Error!)
            : loan.Value.QuoteEarlyRepayment(principal, BankTime.DateOf(clock.GetUtcNow()));
    }

    public Task<Result> PayInstallmentAsync(LoanId id, CancellationToken cancellationToken) =>
        ChangeAsync(id, (loan, by, now) => loan.PayInstallment(by, BankTime.DateOf(now), now), cancellationToken);

    public Task<Result> RepayEarlyAsync(LoanId id, decimal? principal, CancellationToken cancellationToken) =>
        ChangeAsync(id, (loan, by, now) => loan.RepayEarly(principal, by, BankTime.DateOf(now), now), cancellationToken);

    private async Task<Result> ChangeAsync(
        LoanId id, Func<Loan, Domain.Users.UserId, DateTimeOffset, Result> change, CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    var loan = await LoadVisibleAsync(id, Permissions.Loans.RecordRepayment, token);
                    if (loan.IsFailure)
                    {
                        return Result.Failure(loan.Error!);
                    }

                    var profile = (await access.GetAsync(token))!;
                    var result = change(loan.Value, profile.UserId, clock.GetUtcNow());
                    if (result.IsSuccess)
                    {
                        await unitOfWork.SaveChangesAsync(token);
                    }

                    return result;
                },
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure(CommonErrors.ConcurrencyConflict);
        }
    }

    /// <summary>Permission, then the loan through its application's visibility (hidden = not found).</summary>
    private async Task<Result<Loan>> LoadVisibleAsync(LoanId id, string permission, CancellationToken cancellationToken)
    {
        var profile = await access.GetAsync(cancellationToken);
        if (profile is null)
        {
            return Result.Failure<Loan>(CommonErrors.NotAuthenticated);
        }

        if (!profile.HasPermission(permission))
        {
            return Result.Failure<Loan>(CommonErrors.AccessDenied);
        }

        var loan = await loans.GetAsync(id, cancellationToken);
        var application = loan is null ? null : await applications.GetAsync(loan.ApplicationId, cancellationToken);
        return loan is not null && application is not null && ApplicationAccessFilter.For(profile).CanSee(application)
            ? Result.Success(loan)
            : Result.Failure<Loan>(LoanErrors.NotFound);
    }
}
