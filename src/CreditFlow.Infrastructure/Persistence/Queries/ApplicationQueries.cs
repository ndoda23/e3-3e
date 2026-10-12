using CreditFlow.Application.Applications;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Queries;

/// <summary>
/// Application lists, inboxes and details. The access filter is applied in SQL (<see cref="Visible"/>) before paging,
/// so the database returns only rows the viewer may see and the counts are right.
/// </summary>
internal sealed class ApplicationQueries(CreditFlowDbContext db) : IApplicationQueries
{
    public async Task<PagedResult<ApplicationListItem>> SearchAsync(
        ApplicationSearch search, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return new PagedResult<ApplicationListItem>([], 0, search.Page, search.PageSize);
        }

        var rows = Rows(Visible(access));
        if (search.Status is { } status)
        {
            rows = rows.Where(r => r.Status == status);
        }

        if (search.ClientId is { } clientId)
        {
            rows = rows.Where(r => db.LoanApplications.Any(a => a.Id == r.Id && a.Parties.Any(p => p.ClientId == clientId)));
        }

        if (search.ProductId is { } productId)
        {
            rows = rows.Where(r => r.ProductId == productId);
        }

        if (search.Text is { } text)
        {
            rows = PersonalNumber.Create(text) is { IsSuccess: true } number
                ? rows.Where(r => r.PersonalNumber == number.Value)
                : rows.Where(r => r.BorrowerName.Contains(text) || r.ContractNumber == text);
        }

        rows = (search.Sort, search.Descending) switch
        {
            (ApplicationSortField.Created, false) => rows.OrderBy(r => r.CreatedAt),
            (ApplicationSortField.Created, true) => rows.OrderByDescending(r => r.CreatedAt),
            (ApplicationSortField.Amount, false) => rows.OrderBy(r => r.AmountGel),
            (ApplicationSortField.Amount, true) => rows.OrderByDescending(r => r.AmountGel),
            (ApplicationSortField.Status, false) => rows.OrderBy(r => r.Status).ThenByDescending(r => r.StatusChangedAt),
            (ApplicationSortField.Status, true) => rows.OrderByDescending(r => r.Status).ThenByDescending(r => r.StatusChangedAt),
            (ApplicationSortField.Borrower, false) => rows.OrderBy(r => r.BorrowerName),
            (ApplicationSortField.Borrower, true) => rows.OrderByDescending(r => r.BorrowerName),
            (_, false) => rows.OrderBy(r => r.StatusChangedAt),
            _ => rows.OrderByDescending(r => r.StatusChangedAt),
        };

        return await PageAsync(rows, search.Page, search.PageSize, cancellationToken);
    }

    public async Task<PagedResult<ApplicationListItem>> InboxAsync(
        Inbox inbox, ApplicationAccessFilter access, ApprovalTier? maxTier, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return new PagedResult<ApplicationListItem>([], 0, page, pageSize);
        }

        var me = access.Viewer;

        // Never the viewer's own applications, never one they already decided (four-eyes).
        var candidates = Visible(access).Where(a => a.CreatedBy != me && !a.Decisions.Any(d => d.DecidedBy == me));
        candidates = inbox switch
        {
            Inbox.Compliance => candidates.Where(a => a.Status == ApplicationStatus.ComplianceReview),
            Inbox.Committee => candidates.Where(a => a.Status == ApplicationStatus.CreditCommittee),
            Inbox.Underwriting when maxTier >= ApprovalTier.SeniorUnderwriter =>
                candidates.Where(a => a.Status == ApplicationStatus.Underwriting),
            Inbox.Underwriting when maxTier == ApprovalTier.Underwriter =>
                candidates.Where(a => a.Status == ApplicationStatus.Underwriting
                                      && a.Tier == ApprovalTier.Underwriter && a.EscalatedAt == null),
            _ => candidates.Where(_ => false),
        };

        // Oldest first: the queue order.
        return await PageAsync(Rows(candidates).OrderBy(r => r.StatusChangedAt), page, pageSize, cancellationToken);
    }

    public async Task<ApplicationDetails?> GetDetailsAsync(
        LoanApplicationId id, ApplicationAccessFilter access, CancellationToken cancellationToken)
    {
        if (access.SeesNothing)
        {
            return null;
        }

        var application = await Visible(access)
            .Include(a => a.Parties)
            .Include(a => a.Collaterals)
            .Include(a => a.Documents.OrderBy(d => d.UploadedAt).ThenBy(d => d.Id))
            .Include(a => a.History.OrderBy(h => h.Id))
            .Include(a => a.Decisions.OrderBy(d => d.DecidedAt).ThenBy(d => d.Id))
            .Include(a => a.Conditions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return null; // missing or not visible: the same answer
        }

        var summary = await PageAsync(Rows(db.LoanApplications.AsNoTracking().Where(a => a.Id == id)).OrderBy(r => r.CreatedAt), 1, 1, cancellationToken);
        var clientIds = application.Parties.Select(p => p.ClientId).ToList();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.PersonalNumber })
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var userIds = application.History.Select(h => h.By).OfType<UserId>()
            .Concat(application.Decisions.Select(d => d.DecidedBy))
            .Concat(application.Documents.Select(d => d.UploadedBy))
            .Append(application.CreatedBy)
            .Select(u => u.Value)
            .Distinct()
            .ToList();
        var names = await db.UserDirectory.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        var product = await db.LoanProducts.AsNoTracking().Include(p => p.Versions).SingleAsync(p => p.Id == application.ProductId, cancellationToken);
        var payout = await db.Disbursements.AsNoTracking()
            .Where(d => d.ApplicationId == id)
            .Select(d => new PayoutView(d.Status, d.Amount, d.Attempts, d.TransactionId, d.LastError, d.RequestedAt, d.CompletedAt))
            .FirstOrDefaultAsync(cancellationToken);
        var requiredVotes = await db.ApprovalThresholds.AsNoTracking().Select(t => t.RequiredVotes).FirstAsync(cancellationToken);

        string NameOf(UserId? user) => user is { } u ? names.GetValueOrDefault(u.Value, "Unknown user") : "System";

        return new ApplicationDetails
        {
            Summary = summary.Items[0],
            CreatedBy = application.CreatedBy,
            CreatedByName = NameOf(application.CreatedBy),
            Snapshot = application.Snapshot,
            ProductCode = product.Code,
            Terms = application.Terms,
            ProposedTerms = application.ProposedTerms,
            RequiredTier = application.RequiredTier,
            ContractNumber = application.ContractNumber,
            DisbursementRequested = application.DisbursementRequested,
            Parties = application.Parties
                .Select(p => new PartyView(p.ClientId, $"{clients[p.ClientId].FirstName} {clients[p.ClientId].LastName}", clients[p.ClientId].PersonalNumber.Value, p.Role))
                .OrderBy(p => p.Role)
                .ToList(),
            Collaterals = application.Collaterals
                .Select(c => new CollateralView(c.Id, c.Type, c.Description, c.MarketValue, c.ValuationDate))
                .ToList(),
            Documents = application.Documents
                .Select(d => new DocumentView(d.Id, d.Type, d.FileName, d.Size, NameOf(d.UploadedBy), d.UploadedAt))
                .ToList(),
            MissingDocuments = MissingDocuments(application, product),
            History = application.History.Select(h => new HistoryView(h.From, h.To, h.At, NameOf(h.By), h.Reason)).ToList(),
            Decisions = application.Decisions
                .Select(d => new DecisionView(d.Kind, NameOf(d.DecidedBy), d.DecidedBy, d.DecidedAt, d.Reason, d.OverrideReason))
                .ToList(),
            Conditions = application.Conditions
                .Select(c => new ConditionView(c.Id, c.Type, c.Description, c.IsMet, c.MetAt))
                .OrderBy(c => c.Description)
                .ToList(),
            LatestScoring = application.LatestScoring,
            Payout = payout,
            RequiredVotes = requiredVotes,
        };
    }

    /// <summary>
    /// The checklist still open: from the snapshot once submitted; for a draft from the product's current version and the
    /// requested amount (GEL drafts; a foreign-currency draft learns its GEL amount at submission).
    /// </summary>
    private static IReadOnlyList<Domain.Products.DocumentType> MissingDocuments(LoanApplication application, Domain.Products.LoanProduct product)
    {
        if (application.Snapshot is { } snapshot)
        {
            return application.MissingDocuments(snapshot.Terms);
        }

        var amount = application.Terms.Amount.Currency == Currency.GEL ? application.Terms.Amount.Amount : 0m;
        var uploaded = application.Documents.Select(d => d.Type).ToHashSet();
        return product.CurrentVersion.Terms.DocumentsRequiredFor(amount).Where(t => !uploaded.Contains(t)).ToList();
    }

    /// <summary>The access filter as SQL: nothing, one branch, or the whole bank.</summary>
    private IQueryable<LoanApplication> Visible(ApplicationAccessFilter access)
    {
        var applications = db.LoanApplications.AsNoTracking();
        return access.Branch is { } branch ? applications.Where(a => a.BranchId == branch) : applications;
    }

    private IQueryable<Row> Rows(IQueryable<LoanApplication> applications) =>
        from a in applications
        join c in db.Clients on a.BorrowerId equals c.Id
        join p in db.LoanProducts on a.ProductId equals p.Id
        join b in db.Branches on a.BranchId equals b.Id
        select new Row
        {
            Id = a.Id,
            Status = a.Status,
            ProductId = a.ProductId,
            ProductName = p.Name,
            BorrowerName = c.FirstName + " " + c.LastName,
            PersonalNumber = c.PersonalNumber,
            ContractNumber = a.ContractNumber,
            Amount = a.Terms.Amount.Amount,
            Currency = a.Terms.Amount.Currency,
            AmountGel = a.AmountGel,
            TermMonths = a.Terms.TermMonths,
            BranchName = b.Name,
            Tier = a.Tier,
            CreatedAt = a.CreatedAt,
            StatusChangedAt = a.StatusChangedAt,
            EscalatedAt = a.EscalatedAt,
            OfferExpiresAt = a.OfferExpiresAt,
        };

    private static async Task<PagedResult<ApplicationListItem>> PageAsync(
        IQueryable<Row> rows, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<ApplicationListItem>(items.Select(ToItem).ToList(), total, page, pageSize);
    }

    private static ApplicationListItem ToItem(Row r) => new(
        r.Id, r.Id.Reference, r.Status, r.ProductName, r.BorrowerName, r.PersonalNumber.Value,
        Money.Of(r.Amount, r.Currency), r.AmountGel, r.TermMonths, r.BranchName, r.Tier, r.CreatedAt, r.StatusChangedAt,
        r.EscalatedAt, r.OfferExpiresAt);

    private sealed class Row
    {
        public LoanApplicationId Id { get; init; }

        public ApplicationStatus Status { get; init; }

        public Domain.Products.LoanProductId ProductId { get; init; }

        public string ProductName { get; init; } = string.Empty;

        public string BorrowerName { get; init; } = string.Empty;

        public PersonalNumber PersonalNumber { get; init; }

        public string? ContractNumber { get; init; }

        public decimal Amount { get; init; }

        public Currency Currency { get; init; }

        public decimal? AmountGel { get; init; }

        public int TermMonths { get; init; }

        public string BranchName { get; init; } = string.Empty;

        public ApprovalTier? Tier { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset StatusChangedAt { get; init; }

        public DateTimeOffset? EscalatedAt { get; init; }

        public DateTimeOffset? OfferExpiresAt { get; init; }
    }
}
