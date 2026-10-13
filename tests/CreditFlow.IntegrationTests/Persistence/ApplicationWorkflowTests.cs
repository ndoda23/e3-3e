using CreditFlow.Application.Applications;
using CreditFlow.Application.Clients;
using CreditFlow.Application.Common;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.ExternalServices;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CreditFlow.IntegrationTests.Persistence;

/// <summary>
/// The application services end to end on SQL Server, each call in its own scope as a seeded persona: the full flow
/// from registration to approval, branch scope and IDOR, four-eyes, the tiered inboxes, concurrent decisions, the
/// Auditor's read-only access and the upload rules.
/// </summary>
public sealed class ApplicationWorkflowTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    private const string VakeOperator = "anna.gelashvili";
    private const string OtherVakeOperator = "salome.janelidze";
    private const string BatumiOperator = "davit.maisuradze";
    private const string Underwriter = "nika.beridze";
    private const string OtherUnderwriter = "giorgi.kapanadze";
    private const string SeniorUnderwriter = "tamta.tatishvili";
    private const string ComplianceOfficer = "nino.kvaratskhelia";
    private const string Auditor = "levan.abashidze";
    private static readonly string[] CommitteeMembers = ["irakli.tsereteli", "sophio.gogichaishvili", "zurab.kapanadze"];

    private static readonly DateOnly Today = new(2026, 10, 12);
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[512]];

    private readonly FakeTimeProvider _clock = new(BankTime.ToInstant(Today, new TimeOnly(10, 0)));
    private TestLending? _lending;

    protected override string Environment => "Development";

    protected override bool SeedDevUsers => true;

    protected override TimeProvider? Clock => _clock;

    private OutboxProcessor Processor => Host.Services.GetRequiredService<OutboxProcessor>();

    [Fact]
    public async Task An_application_goes_from_registration_through_pre_check_and_scoring_to_approval()
    {
        var anna = await UserAsync(VakeOperator);
        var id = await SubmitThroughServicesAsync(anna, MockPersonalNumbers.Clean, 5_000m);

        await Processor.ProcessAllDueAsync(Ct);
        Assert.Equal(ApplicationStatus.DocumentsPending, await StatusAsync(id));

        AssertSuccess(await AsAsync<ApplicationService, Result>(anna, s => s.SendToScoringAsync(id, Ct)));
        await Processor.ProcessAllDueAsync(Ct);
        Assert.Equal(ApplicationStatus.Underwriting, await StatusAsync(id));

        var nika = await UserAsync(Underwriter);
        var inbox = await AsAsync<DecisionService, PagedResult<ApplicationListItem>>(nika, s => s.InboxAsync(Inbox.Underwriting, 1, Ct));
        Assert.Contains(inbox.Items, i => i.Id == id);

        AssertSuccess(await AsAsync<DecisionService, Result>(nika, s => s.ApproveAsync(id, [], null, Ct)));

        var details = (await AsAsync<ApplicationService, ApplicationDetails?>(anna, s => s.GetDetailsAsync(id, Ct)))!;
        Assert.Equal(ApplicationStatus.Approved, details.Summary.Status);
        Assert.Equal(ApprovalTier.Underwriter, details.Summary.Tier);
        Assert.Equal(2, details.Documents.Count);
        Assert.Empty(details.MissingDocuments);
        Assert.NotNull(details.LatestScoring);
        Assert.All(details.LatestScoring.Results, r => Assert.True(r.Passed, r.Reason));
        Assert.Equal(
            [ApplicationStatus.Draft, ApplicationStatus.Submitted, ApplicationStatus.PreCheck, ApplicationStatus.DocumentsPending,
             ApplicationStatus.Scoring, ApplicationStatus.Underwriting, ApplicationStatus.Approved],
            details.History.Select(h => h.To));
        Assert.Equal(DecisionKind.Approve, Assert.Single(details.Decisions).Kind);

        var download = await AsAsync<ApplicationService, DocumentContent?>(anna, s => s.DownloadAsync(id, details.Documents[0].Id, Ct));
        Assert.Equal(Pdf, download!.Content);
        Assert.Equal("application/pdf", download.ContentType);

        var audit = await Host.QueryAsync(db => db.AuditLogs
            .Where(a => a.EntityType == "Application" && a.EntityId == id.ToString())
            .ToListAsync(Ct));
        Assert.Equal(details.History.Count, audit.Count(a => a.Action == AuditActions.ApplicationStatusChanged));
        Assert.Contains(audit, a => a.Action == AuditActions.ApplicationScored && a.AfterJson!.Contains("PTI", StringComparison.Ordinal));
        Assert.Contains(audit, a => a.Action == AuditActions.ApplicationDecision && a.UserId == nika.Value);
    }

    [Fact]
    public async Task Applications_created_in_the_same_instant_get_different_references_everywhere()
    {
        // The clock does not move: both applications are created at the same instant (and within the same
        // millisecond of their time-ordered ids).
        var anna = await UserAsync(VakeOperator);
        var first = await SubmitThroughServicesAsync(anna, NextPersonalNumber(), 5_000m);
        var second = await SubmitThroughServicesAsync(anna, NextPersonalNumber(), 5_000m);
        await Processor.ProcessAllDueAsync(Ct);

        var references = new List<string>();
        foreach (var id in new[] { first, second })
        {
            var details = (await AsAsync<ApplicationService, ApplicationDetails?>(anna, s => s.GetDetailsAsync(id, Ct)))!;
            Assert.Matches("^APP-[0-9]{6,}$", details.Summary.Reference);
            references.Add(details.Summary.Reference);

            var found = await AsAsync<ApplicationService, PagedResult<ApplicationListItem>>(anna, s =>
                s.SearchAsync(new ApplicationSearch { Text = details.Summary.Reference.ToLowerInvariant() }, Ct));
            Assert.Equal(id, Assert.Single(found.Items).Id);
        }

        Assert.NotEqual(references[0], references[1]);
        var subjects = await Host.QueryAsync(db => db.Set<NotificationRecord>().Select(n => n.Subject).Distinct().ToListAsync(Ct));
        Assert.Contains($"CreditFlow · Application {references[0]}: Submitted", subjects);
        Assert.Contains($"CreditFlow · Application {references[1]}: Submitted", subjects);
    }

    [Fact]
    public async Task The_client_audit_record_masks_phone_and_personal_number()
    {
        var anna = await UserAsync(VakeOperator);
        await CreateDraftAsync(anna, "01001011099");

        var audit = await Host.QueryAsync(db => db.AuditLogs.Where(a => a.EntityType == "Client").Select(a => a.AfterJson).ToListAsync(Ct));

        var registration = Assert.Single(audit, json => json!.Contains("PersonalNumber", StringComparison.Ordinal))!;
        Assert.DoesNotContain("01001011099", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("555 000000", registration, StringComparison.Ordinal);
        Assert.Contains("****00", registration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_screening_match_waits_for_compliance_which_clears_it()
    {
        var anna = await UserAsync(VakeOperator);
        var id = await SubmitThroughServicesAsync(anna, MockPersonalNumbers.Sanctioned, 5_000m);
        await Processor.ProcessAllDueAsync(Ct);
        Assert.Equal(ApplicationStatus.ComplianceReview, await StatusAsync(id));

        var nino = await UserAsync(ComplianceOfficer);
        var inbox = await AsAsync<DecisionService, PagedResult<ApplicationListItem>>(nino, s => s.InboxAsync(Inbox.Compliance, 1, Ct));
        Assert.Contains(inbox.Items, i => i.Id == id);

        AssertSuccess(await AsAsync<DecisionService, Result>(nino, s => s.DecideComplianceAsync(id, true, "False positive: different date of birth.", Ct)));

        Assert.Equal(ApplicationStatus.DocumentsPending, await StatusAsync(id));
    }

    [Fact]
    public async Task After_a_counter_offer_the_client_accepts_and_a_different_underwriter_decides()
    {
        var anna = await UserAsync(VakeOperator);
        var id = await SubmitThroughServicesAsync(anna, MockPersonalNumbers.Clean, 8_000m);
        await Processor.ProcessAllDueAsync(Ct);
        AssertSuccess(await AsAsync<ApplicationService, Result>(anna, s => s.SendToScoringAsync(id, Ct)));
        await Processor.ProcessAllDueAsync(Ct);

        var nika = await UserAsync(Underwriter);
        AssertSuccess(await AsAsync<DecisionService, Result>(nika, s => s.CounterOfferAsync(id, 6_000m, 24, 20m, "Lower amount.", Ct)));
        _clock.Advance(TimeSpan.FromHours(2)); // the client answers later; decisions are listed in time order
        AssertSuccess(await AsAsync<ApplicationService, Result>(anna, s => s.RecordClientResponseAsync(id, true, null, Ct)));
        await Processor.ProcessAllDueAsync(Ct);
        Assert.Equal(ApplicationStatus.Underwriting, await StatusAsync(id));

        // One decision per person per application: the underwriter who made the offer neither sees nor decides it again.
        Assert.DoesNotContain((await InboxAsync(nika, Inbox.Underwriting)).Items, i => i.Id == id);
        Assert.Equal(ApplicationErrors.AlreadyDecided, (await AsAsync<DecisionService, Result>(nika, s => s.ApproveAsync(id, [], null, Ct))).Error);

        var giorgi = await UserAsync(OtherUnderwriter);
        Assert.Contains((await InboxAsync(giorgi, Inbox.Underwriting)).Items, i => i.Id == id);
        AssertSuccess(await AsAsync<DecisionService, Result>(giorgi, s => s.ApproveAsync(id, [], null, Ct)));

        var details = (await AsAsync<ApplicationService, ApplicationDetails?>(anna, s => s.GetDetailsAsync(id, Ct)))!;
        Assert.Equal(ApplicationStatus.Approved, details.Summary.Status);
        Assert.Equal(6_000m, details.Terms.Amount.Amount);
        Assert.Equal([DecisionKind.CounterOffer, DecisionKind.Approve], details.Decisions.Select(d => d.Kind));
        Assert.Equal([nika, giorgi], details.Decisions.Select(d => d.DecidedById));
        Assert.Equal(2, await Host.QueryAsync(db => db.AuditLogs.CountAsync(
            a => a.EntityId == id.ToString() && a.Action == AuditActions.ApplicationScored, Ct)));
    }

    [Fact]
    public async Task An_operator_sees_the_applications_of_their_branch_only_in_lists_and_by_id()
    {
        var anna = await UserAsync(VakeOperator);
        var id = await CreateDraftAsync(anna);
        var documentId = await UploadAsync(anna, id, DocumentType.IdentityDocumentCopy);

        var salome = await UserAsync(OtherVakeOperator);
        Assert.Contains((await SearchAsync(salome)).Items, i => i.Id == id);

        var davit = await UserAsync(BatumiOperator);
        var batumiList = await SearchAsync(davit);
        Assert.DoesNotContain(batumiList.Items, i => i.Id == id);
        Assert.Equal(0, batumiList.TotalCount);
        Assert.Null(await AsAsync<ApplicationService, ApplicationDetails?>(davit, s => s.GetDetailsAsync(id, Ct)));
        Assert.Null(await AsAsync<ApplicationService, DocumentContent?>(davit, s => s.DownloadAsync(id, documentId, Ct)));

        // Commands by id from another branch: "not found", never a change.
        Assert.Equal(ApplicationErrors.NotFound, (await AsAsync<ApplicationService, Result>(davit, s => s.CancelAsync(id, "Not mine.", Ct))).Error);
        Assert.Equal(ApplicationErrors.NotFound, (await AsAsync<ApplicationService, Result>(davit, s => s.SubmitAsync(id, Ct))).Error);
        Assert.Equal(ApplicationErrors.NotFound, (await AsAsync<ApplicationService, Result>(davit, s => s.RemoveDocumentAsync(id, documentId, Ct))).Error);
        Assert.Equal(ApplicationStatus.Draft, await StatusAsync(id));
    }

    [Fact]
    public async Task An_underwriter_never_gets_an_application_they_created_and_cannot_decide_it()
    {
        var nika = await UserAsync(Underwriter);
        var id = await InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, createdBy: nika);

        Assert.DoesNotContain((await InboxAsync(nika, Inbox.Underwriting)).Items, i => i.Id == id);
        Assert.Equal(ApplicationErrors.CreatorCannotDecide, (await AsAsync<DecisionService, Result>(nika, s => s.ApproveAsync(id, [], null, Ct))).Error);
        Assert.Equal(ApplicationErrors.CreatorCannotDecide, (await AsAsync<DecisionService, Result>(nika, s => s.RejectAsync(id, "No.", Ct))).Error);

        var giorgi = await UserAsync(OtherUnderwriter);
        Assert.Contains((await InboxAsync(giorgi, Inbox.Underwriting)).Items, i => i.Id == id);
        Assert.Equal(ApplicationStatus.Underwriting, await StatusAsync(id));
    }

    [Fact]
    public async Task The_underwriting_inbox_shows_each_decider_the_files_of_their_tier()
    {
        var creator = await UserAsync(VakeOperator);
        var small = await InReviewAsync(LendingSeedCatalog.Consumer, 20_000m, creator);
        var large = await InReviewAsync(LendingSeedCatalog.Consumer, 20_000.01m, creator);

        var nika = await UserAsync(Underwriter);
        var underwriterInbox = await InboxAsync(nika, Inbox.Underwriting);
        Assert.Contains(underwriterInbox.Items, i => i.Id == small);
        Assert.DoesNotContain(underwriterInbox.Items, i => i.Id == large);
        Assert.Equal(ApplicationErrors.AuthorityTooLow, (await AsAsync<DecisionService, Result>(nika, s => s.ApproveAsync(large, [], null, Ct))).Error);

        var tamta = await UserAsync(SeniorUnderwriter);
        var seniorInbox = await InboxAsync(tamta, Inbox.Underwriting);
        Assert.Contains(seniorInbox.Items, i => i.Id == small);
        Assert.Contains(seniorInbox.Items, i => i.Id == large);
        AssertSuccess(await AsAsync<DecisionService, Result>(tamta, s => s.ApproveAsync(large, [], null, Ct)));
    }

    [Fact]
    public async Task Two_underwriters_approving_at_the_same_time_produce_one_decision()
    {
        var id = await InReviewAsync(LendingSeedCatalog.Consumer, 10_000m, await UserAsync(VakeOperator));
        var deciders = new[] { await UserAsync(Underwriter), await UserAsync(OtherUnderwriter) };

        var results = await Task.WhenAll(deciders.Select(user => Task.Run(
            () => AsAsync<DecisionService, Result>(user, s => s.ApproveAsync(id, [], null, Ct)), Ct)));

        // The loser either lost the rowversion race or loaded the file after the winner had decided it.
        Assert.Single(results, r => r.IsSuccess);
        var loser = Assert.Single(results, r => r.IsFailure).Error!;
        Assert.Contains(loser, new[] { CommonErrors.ConcurrencyConflict, ApplicationErrors.WrongStatus(ApplicationStatus.Approved) });

        var decisions = await DecisionsAsync(id);
        Assert.Single(decisions, d => d.Kind == DecisionKind.Approve);
        Assert.Equal(ApplicationStatus.Approved, await StatusAsync(id));
    }

    [Fact]
    public async Task Three_committee_members_voting_at_the_same_time_lose_no_vote()
    {
        var id = await InReviewAsync(LendingSeedCatalog.Mortgage, 150_000m, await UserAsync(VakeOperator));
        Assert.Equal(ApplicationStatus.CreditCommittee, await StatusAsync(id));
        var members = new List<UserId>();
        foreach (var member in CommitteeMembers)
        {
            members.Add(await UserAsync(member));
            Assert.Contains((await InboxAsync(members[^1], Inbox.Committee)).Items, i => i.Id == id);
        }

        var results = await Task.WhenAll(members.Select(user => Task.Run(
            () => AsAsync<DecisionService, Result>(user, s => s.VoteAsync(id, true, null, Ct)), Ct)));

        // Every vote that reported success is stored; two matching votes approve the file.
        var votes = (await DecisionsAsync(id)).Where(d => d.Kind == DecisionKind.VoteApprove).ToList();
        Assert.True(results.Count(r => r.IsSuccess) >= 2);
        Assert.Equal(results.Count(r => r.IsSuccess), votes.Count);
        Assert.Equal(votes.Count, votes.Select(v => v.DecidedBy).Distinct().Count());
        Assert.Equal(ApplicationStatus.Approved, await StatusAsync(id));
    }

    [Fact]
    public async Task The_auditor_reads_everything_and_changes_nothing()
    {
        var anna = await UserAsync(VakeOperator);
        var id = await CreateDraftAsync(anna);
        var documentId = await UploadAsync(anna, id, DocumentType.IdentityDocumentCopy);
        var clientId = (await Host.QueryAsync(db => db.LoanApplications.Where(a => a.Id == id).Select(a => a.BorrowerId).SingleAsync(Ct)));
        var productId = await ProductIdAsync(LendingSeedCatalog.Consumer);
        var levan = await UserAsync(Auditor);

        Assert.Contains((await SearchAsync(levan)).Items, i => i.Id == id);
        Assert.NotNull(await AsAsync<ApplicationService, ApplicationDetails?>(levan, s => s.GetDetailsAsync(id, Ct)));
        Assert.NotNull(await AsAsync<ApplicationService, DocumentContent?>(levan, s => s.DownloadAsync(id, documentId, Ct)));
        Assert.NotNull(await AsAsync<ClientService, ClientDetailsView?>(levan, s => s.GetAsync(clientId, Ct)));

        var applicationCommands = new Func<ApplicationService, Task<Result>>[]
        {
            async s => Untyped(await s.CreateAsync(new CreateApplicationCommand(clientId, productId, 5_000m, Currency.GEL, 24, 18m, ScheduleType.Annuity), Ct)),
            s => s.ChangeTermsAsync(id, 6_000m, Currency.GEL, 24, 18m, ScheduleType.Annuity, Ct),
            s => s.AddPartyAsync(id, clientId, PartyRole.Guarantor, Ct),
            s => s.RemovePartyAsync(id, clientId, Ct),
            s => s.AddCollateralAsync(id, new AddCollateralCommand(CollateralType.Vehicle, "Car", 10_000m, Currency.GEL, Today), Ct),
            s => s.RemoveCollateralAsync(id, ApplicationLineId.New(), Ct),
            s => s.UploadDocumentAsync(id, DocumentType.IncomeStatement, "income.pdf", Pdf, Ct),
            s => s.RemoveDocumentAsync(id, documentId, Ct),
            s => s.SubmitAsync(id, Ct),
            s => s.SendToScoringAsync(id, Ct),
            s => s.RecordClientResponseAsync(id, true, null, Ct),
            s => s.CancelAsync(id, "Auditor.", Ct),
        };
        var decisionCommands = new Func<DecisionService, Task<Result>>[]
        {
            s => s.DecideComplianceAsync(id, true, "Auditor.", Ct),
            s => s.ApproveAsync(id, [], "Auditor.", Ct),
            s => s.RejectAsync(id, "Auditor.", Ct),
            s => s.CounterOfferAsync(id, 4_000m, 24, 18m, "Auditor.", Ct),
            s => s.VoteAsync(id, true, null, Ct),
        };
        var clientCommands = new Func<ClientService, Task<Result>>[]
        {
            async s => Untyped(await s.RegisterAsync(RegisterCommand(NextPersonalNumber()), Ct)),
            s => s.UpdateDetailsAsync(clientId, Details("Changed"), Ct),
            s => s.ReplaceDocumentAsync(clientId, IdentityDocumentType.Passport, "P1234567", new DateOnly(2024, 1, 1), new DateOnly(2034, 1, 1), Ct),
            s => s.AddIncomeAsync(clientId, IncomeType.Salary, "Employer", 1_000m, Currency.GEL, Ct),
            s => s.RemoveIncomeAsync(clientId, ClientLineId.New(), Ct),
            s => s.AddObligationAsync(clientId, "Other bank", 100m, 1_000m, Currency.GEL, Ct),
            s => s.RemoveObligationAsync(clientId, ClientLineId.New(), Ct),
        };

        var results = new List<Result>();
        foreach (var command in applicationCommands)
        {
            results.Add(await AsAsync(levan, command));
        }

        foreach (var command in decisionCommands)
        {
            results.Add(await AsAsync(levan, command));
        }

        foreach (var command in clientCommands)
        {
            results.Add(await AsAsync(levan, command));
        }

        Assert.All(results, r => Assert.Equal(CommonErrors.AccessDenied, r.Error));
        var details = (await AsAsync<ApplicationService, ApplicationDetails?>(anna, s => s.GetDetailsAsync(id, Ct)))!;
        Assert.Equal(ApplicationStatus.Draft, details.Summary.Status);
        Assert.Single(details.Documents);
        Assert.Equal(1, await Host.QueryAsync(db => db.LoanApplications.CountAsync(Ct)));
        Assert.Equal(1, await Host.QueryAsync(db => db.Clients.CountAsync(Ct)));
    }

    [Fact]
    public async Task Uploads_are_checked_by_content_and_size()
    {
        var anna = await UserAsync(VakeOperator);
        var id = await CreateDraftAsync(anna);

        Task<Result> Upload(string name, byte[] content) =>
            AsAsync<ApplicationService, Result>(anna, s => s.UploadDocumentAsync(id, DocumentType.IncomeStatement, name, content, Ct));

        Assert.Equal(DocumentUploadPolicy.UnsupportedType, (await Upload("income.pdf", "MZ not a pdf"u8.ToArray())).Error);
        Assert.Equal(DocumentUploadPolicy.Empty, (await Upload("income.pdf", [])).Error);
        var tooLarge = new byte[DocumentUploadPolicy.MaxBytes + 1];
        Pdf.CopyTo(tooLarge, 0);
        Assert.Equal(DocumentUploadPolicy.TooLarge, (await Upload("income.pdf", tooLarge)).Error);

        // A PNG with a .pdf name is accepted as what it is; a path in the name is cut to the file name.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        AssertSuccess(await Upload(@"C:\Users\x\scan.pdf", png));

        var document = Assert.Single((await AsAsync<ApplicationService, ApplicationDetails?>(anna, s => s.GetDetailsAsync(id, Ct)))!.Documents);
        Assert.Equal("scan.pdf", document.FileName);
        Assert.Equal(png.Length, document.Size);
        var stored = await AsAsync<ApplicationService, DocumentContent?>(anna, s => s.DownloadAsync(id, document.Id, Ct));
        Assert.Equal("image/png", stored!.ContentType);
    }

    // ----- Helpers -----

    private static void AssertSuccess(Result result) => Assert.True(result.IsSuccess, result.Error?.Description);

    private static Result Untyped<T>(Result<T> result) => result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);

    private Task<TResult> AsAsync<TService, TResult>(UserId user, Func<TService, Task<TResult>> call)
        where TService : notnull =>
        Host.InScopeAsAsync(user, sp => call(sp.GetRequiredService<TService>()));

    private Task<PagedResult<ApplicationListItem>> InboxAsync(UserId user, Inbox inbox) =>
        AsAsync<DecisionService, PagedResult<ApplicationListItem>>(user, s => s.InboxAsync(inbox, 1, Ct));

    private Task<PagedResult<ApplicationListItem>> SearchAsync(UserId user) =>
        AsAsync<ApplicationService, PagedResult<ApplicationListItem>>(user, s => s.SearchAsync(new ApplicationSearch(), Ct));

    private Task<UserId> UserAsync(string userName) =>
        Host.QueryAsync(async db => new UserId(
            await db.UserDirectory.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(Ct)));

    private Task<LoanProductId> ProductIdAsync(string code) =>
        Host.QueryAsync(db => db.LoanProducts.Where(p => p.Code == code).Select(p => p.Id).SingleAsync(Ct));

    private Task<ApplicationStatus> StatusAsync(LoanApplicationId id) =>
        Host.QueryAsync(db => db.LoanApplications.Where(a => a.Id == id).Select(a => a.Status).SingleAsync(Ct));

    private Task<List<ApplicationDecision>> DecisionsAsync(LoanApplicationId id) =>
        Host.QueryAsync(async db => (await db.LoanApplications.AsNoTracking().Include(a => a.Decisions).SingleAsync(a => a.Id == id, Ct))
            .Decisions.ToList());

    private TestLending Lending => _lending ??= new TestLending(Host.Services, _clock);

    private string NextPersonalNumber() => Lending.NextPersonalNumber();

    private static ClientDetails Details(string firstName) =>
        new(firstName, "Client", new DateOnly(1990, 1, 1), "+995 555 000000", null, "Tbilisi");

    private static RegisterClientCommand RegisterCommand(string personalNumber) =>
        new(personalNumber, Details("Test"), IdentityDocumentType.IdCard, "ID" + personalNumber, new DateOnly(2020, 1, 1), new DateOnly(2030, 1, 1));

    /// <summary>A client with a salary and a consumer-loan draft, created by <paramref name="operatorId"/>.</summary>
    private async Task<LoanApplicationId> CreateDraftAsync(UserId operatorId, string? personalNumber = null, decimal amount = 5_000m)
    {
        var clientId = await AsAsync<ClientService, Result<ClientId>>(
            operatorId, s => s.RegisterAsync(RegisterCommand(personalNumber ?? NextPersonalNumber()), Ct));
        AssertSuccess(clientId);
        AssertSuccess(await AsAsync<ClientService, Result>(
            operatorId, s => s.AddIncomeAsync(clientId.Value, IncomeType.Salary, "Employer LLC", 3_000m, Currency.GEL, Ct)));

        var productId = await ProductIdAsync(LendingSeedCatalog.Consumer);
        var created = await AsAsync<ApplicationService, Result<LoanApplicationId>>(operatorId, s => s.CreateAsync(
            new CreateApplicationCommand(clientId.Value, productId, amount, Currency.GEL, 24, 18m, ScheduleType.Annuity), Ct));
        AssertSuccess(created);
        return created.Value;
    }

    private async Task<ApplicationLineId> UploadAsync(UserId operatorId, LoanApplicationId id, DocumentType type)
    {
        AssertSuccess(await AsAsync<ApplicationService, Result>(
            operatorId, s => s.UploadDocumentAsync(id, type, $"{type}.pdf", Pdf, Ct)));
        return (await AsAsync<ApplicationService, ApplicationDetails?>(operatorId, s => s.GetDetailsAsync(id, Ct)))!
            .Documents.Single(d => d.Type == type).Id;
    }

    /// <summary>Registration → draft → the two consumer documents → submitted, all through the services.</summary>
    private async Task<LoanApplicationId> SubmitThroughServicesAsync(UserId operatorId, string personalNumber, decimal amount)
    {
        var id = await CreateDraftAsync(operatorId, personalNumber, amount);
        await UploadAsync(operatorId, id, DocumentType.IdentityDocumentCopy);
        await UploadAsync(operatorId, id, DocumentType.IncomeStatement);
        AssertSuccess(await AsAsync<ApplicationService, Result>(operatorId, s => s.SubmitAsync(id, Ct)));
        return id;
    }

    /// <summary>An application already scored and waiting for a decision, created by <paramref name="createdBy"/>.</summary>
    private Task<LoanApplicationId> InReviewAsync(string productCode, decimal amount, UserId createdBy) =>
        Lending.InReviewAsync(productCode, amount, createdBy);
}
