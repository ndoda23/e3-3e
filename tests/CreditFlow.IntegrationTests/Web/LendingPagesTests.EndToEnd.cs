using System.Net;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Products;
using CreditFlow.Infrastructure.ExternalServices;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>
/// The whole process through the pages, each step by the role that owns it: the operator's wizard, the pre-check and
/// scoring (background), the underwriter's inbox and decision, signing, the payout (also after a rejection and a
/// timeout), the loan and its repayments; the contract and schedule PDFs; read-only and out-of-scope viewers.
/// </summary>
public sealed partial class LendingPagesTests
{
    [Fact]
    public async Task The_whole_process_works_in_the_browser_role_by_role()
    {
        using var operatorSession = await SignInAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);

        // Operator: wizard (borrower and terms → parties → documents → review and submit).
        var id = await CreateDraftAsync(operatorSession);
        var terms = await operatorSession.GetPageAsync($"/Applications/Wizard/{id}?step=1");
        Assert.Contains("data-wizard-step=\"1\"", terms, StringComparison.Ordinal);
        Assert.Contains("data-section=\"terms\"", terms, StringComparison.Ordinal);
        Assert.Contains("data-wizard-step=\"2\"", await operatorSession.GetPageAsync($"/Applications/Wizard/{id}?step=2"), StringComparison.Ordinal);
        var documents = await operatorSession.GetPageAsync($"/Applications/Wizard/{id}?step=3");
        Assert.Contains("data-missing", documents, StringComparison.Ordinal);

        var uploaded = await UploadAsync(operatorSession, id, DocumentType.IdentityDocumentCopy, wizardStep: 3);
        Assert.Equal($"/Applications/Wizard/{id}?step=3", uploaded.Headers.Location!.OriginalString);
        await UploadAsync(operatorSession, id, DocumentType.IncomeStatement, wizardStep: 3);
        var review = await operatorSession.GetPageAsync($"/Applications/Wizard/{id}?step=4");
        Assert.Contains("data-checklist-complete", review, StringComparison.Ordinal);
        Assert.DoesNotContain("data-missing", review, StringComparison.Ordinal);

        var submitted = await PostAsync(operatorSession, $"/Applications/Submit/{id}", new() { ["step"] = "4" });
        Assert.Equal($"/Applications/Wizard/{id}?step=4", submitted.Headers.Location!.OriginalString);
        Assert.Equal($"/Applications/Details/{id}", (await operatorSession.GetAsync($"/Applications/Wizard/{id}?step=4")).Headers.Location!.OriginalString);

        // System: pre-check; operator: send to scoring; system: scoring routes to the underwriter.
        await ProcessOutboxAsync();
        Assert.Equal(ApplicationStatus.DocumentsPending, await PageStatusAsync(id));
        await PostAsync(operatorSession, $"/Applications/SendToScoring/{id}");
        await ProcessOutboxAsync();
        Assert.Equal(ApplicationStatus.Underwriting, await PageStatusAsync(id));

        // Operator cannot decide; the underwriter finds it in the inbox, sees the scoring explanation and approves.
        var operatorView = await operatorSession.GetPageAsync($"/Applications/Details/{id}");
        Assert.DoesNotContain("data-section=\"decide\"", operatorView, StringComparison.Ordinal);
        using var underwriter = await SignInAsync(RoleCodes.Underwriter);
        Assert.Contains($"data-application=\"{id}\"", await underwriter.GetPageAsync("/Inbox"), StringComparison.Ordinal);
        var decisionPage = await underwriter.GetPageAsync($"/Applications/Details/{id}");
        Assert.Contains("data-section=\"decide\"", decisionPage, StringComparison.Ordinal);
        Assert.Contains("data-verdict=\"Passed\"", decisionPage, StringComparison.Ordinal);
        Assert.Contains("data-rule=\"PTI\"", decisionPage, StringComparison.Ordinal);
        Assert.DoesNotContain("data-section=\"workflow\"", decisionPage, StringComparison.Ordinal);
        await PostAsync(underwriter, $"/Applications/Approve/{id}");
        Assert.Equal(ApplicationStatus.Approved, await PageStatusAsync(id));

        // Operator: the offer can be printed; signing numbers the contract; the payout books the loan.
        var offer = await operatorSession.GetAsync($"/Applications/Contract/{id}");
        await AssertPdfAsync(offer);
        await PostAsync(operatorSession, $"/Applications/SignContract/{id}");
        var signed = await operatorSession.GetPageAsync($"/Applications/Details/{id}");
        Assert.Contains("data-contract-number>LN-2026-000001<", signed, StringComparison.Ordinal);
        Assert.Contains("data-disburse=\"New\"", signed, StringComparison.Ordinal);
        await PostAsync(operatorSession, $"/Applications/Disburse/{id}");
        await ProcessOutboxAsync();
        var active = await operatorSession.GetPageAsync($"/Applications/Details/{id}");
        Assert.Contains("data-status=\"Active\"", active, StringComparison.Ordinal);
        Assert.Contains("data-payout=\"Succeeded\"", active, StringComparison.Ordinal);
        Assert.DoesNotContain("data-disburse=", active, StringComparison.Ordinal);
        await AssertPdfAsync(await operatorSession.GetAsync($"/Applications/Schedule/{id}"));

        // Operator: the loan list and page; an installment, then the rest early: loan and application close.
        var loanId = await QueryAsync(db => db.Loans.Where(l => l.ApplicationId == new LoanApplicationId(id)).Select(l => l.Id.Value).SingleAsync(Ct));
        Assert.Contains($"data-loan=\"{loanId}\"", await operatorSession.GetPageAsync("/Loans"), StringComparison.Ordinal);
        await PostAsync(operatorSession, $"/Loans/PayInstallment/{loanId}");
        await operatorSession.PostFormAsync($"/Loans/Details/{loanId}?full=true", $"/Loans/RepayEarly/{loanId}", new Dictionary<string, string> { ["full"] = "true" });
        await ProcessOutboxAsync();
        Assert.Contains("data-loan-status=\"Closed\"", await operatorSession.GetPageAsync($"/Loans/Details/{loanId}"), StringComparison.Ordinal);
        Assert.Equal(ApplicationStatus.Closed, await PageStatusAsync(id));

        // Auditor: reads the application, the loan and the PDFs; no action is offered.
        using var auditor = await SignInAsync(RoleCodes.Auditor);
        var audited = await auditor.GetPageAsync($"/Applications/Details/{id}");
        foreach (var section in new[] { "workflow", "decide" })
        {
            Assert.DoesNotContain($"data-section=\"{section}\"", audited, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("<form method=\"post\" action=\"/Applications/", audited, StringComparison.Ordinal);
        Assert.DoesNotContain("data-section=\"repay\"", await auditor.GetPageAsync($"/Loans/Details/{loanId}"), StringComparison.Ordinal);
        await AssertPdfAsync(await auditor.GetAsync($"/Applications/Contract/{id}"));
    }

    [Fact]
    public async Task Compliance_and_the_credit_committee_decide_from_their_inboxes()
    {
        // A sanctions match stops the application until compliance clears it.
        using var operatorSession = await SignInAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);
        var held = await CreateDraftAsync(operatorSession, MockPersonalNumbers.Sanctioned);
        await PostAsync(operatorSession, $"/Applications/Submit/{held}");
        await ProcessOutboxAsync();
        Assert.Equal(ApplicationStatus.ComplianceReview, await PageStatusAsync(held));

        using var compliance = await SignInAsync(RoleCodes.Compliance);
        var complianceInbox = await compliance.GetPageAsync("/Inbox");
        Assert.Contains("data-current-inbox=\"Compliance\"", complianceInbox, StringComparison.Ordinal);
        Assert.Contains($"data-application=\"{held}\"", complianceInbox, StringComparison.Ordinal);
        await PostAsync(compliance, $"/Applications/Compliance/{held}", new() { ["clear"] = "true", ["reason"] = "Different person." });
        Assert.Equal(ApplicationStatus.DocumentsPending, await PageStatusAsync(held));

        // Above the Senior Underwriter threshold the committee votes; two matching votes approve.
        var (_, creatorId, _) = await CreateUserAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);
        var lending = new TestLending(Factory.Services, Factory.Clock);
        var large = (await lending.InReviewAsync(LendingSeedCatalog.Mortgage, 150_000m, creatorId)).Value;
        using var first = await SignInAsync(RoleCodes.CreditCommittee);
        using var second = await SignInAsync(RoleCodes.CreditCommittee);

        Assert.Contains($"data-application=\"{large}\"", await first.GetPageAsync("/Inbox"), StringComparison.Ordinal);
        await PostAsync(first, $"/Applications/Vote/{large}", new() { ["approve"] = "true" });
        var afterOne = await second.GetPageAsync($"/Applications/Details/{large}");
        Assert.Contains("data-votes=\"1/0\"", afterOne, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-application=\"{large}\"", await first.GetPageAsync("/Inbox"), StringComparison.Ordinal);
        await PostAsync(second, $"/Applications/Vote/{large}", new() { ["approve"] = "true" });

        var approved = await operatorSession.GetPageAsync($"/Applications/Details/{large}");
        Assert.Equal(ApplicationStatus.Approved, await PageStatusAsync(large));
        Assert.Contains("data-votes=\"2/0\"", approved, StringComparison.Ordinal);
        Assert.Contains("data-condition=\"open\"", approved, StringComparison.Ordinal); // the mortgage's standard conditions
    }

    [Fact]
    public async Task A_rejected_payout_shows_the_error_and_a_retry_pays_once()
    {
        var (id, browser) = await OperatorWithApprovedApplicationAsync(MockPersonalNumbers.CoreBankingError);
        using var _ = browser;

        await PostAsync(browser, $"/Applications/SignContract/{id}");
        await PostAsync(browser, $"/Applications/Disburse/{id}");
        var failed = await browser.GetPageAsync($"/Applications/Details/{id}");
        Assert.Contains("data-payout=\"Failed\"", failed, StringComparison.Ordinal);
        Assert.Contains("data-payout-error", failed, StringComparison.Ordinal);
        Assert.Contains("data-disburse=\"Failed\"", failed, StringComparison.Ordinal);

        await PostAsync(browser, $"/Applications/Disburse/{id}");

        var paid = await browser.GetPageAsync($"/Applications/Details/{id}");
        Assert.Contains("data-payout=\"Succeeded\"", paid, StringComparison.Ordinal);
        Assert.Equal(1, await QueryAsync(db => db.Set<MockCoreBankingTransaction>().CountAsync(Ct)));
    }

    [Fact]
    public async Task A_payout_without_an_answer_stays_pending_and_checking_again_confirms_it()
    {
        var (id, browser) = await OperatorWithApprovedApplicationAsync(MockPersonalNumbers.CoreBankingTimeout);
        using var _ = browser;

        await PostAsync(browser, $"/Applications/SignContract/{id}");
        await PostAsync(browser, $"/Applications/Disburse/{id}");
        var pending = await browser.GetPageAsync($"/Applications/Details/{id}");
        Assert.Contains("data-payout=\"Pending\"", pending, StringComparison.Ordinal);
        Assert.Contains("data-disburse=\"Pending\"", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("data-section=\"workflow\"", pending, StringComparison.Ordinal); // no cancel while money may move

        await PostAsync(browser, $"/Applications/Disburse/{id}");

        Assert.Contains("data-payout=\"Succeeded\"", await browser.GetPageAsync($"/Applications/Details/{id}"), StringComparison.Ordinal);
        Assert.Equal(1, await QueryAsync(db => db.Set<MockCoreBankingTransaction>().CountAsync(Ct)));
    }

    [Fact]
    public async Task Documents_and_loans_follow_the_viewers_scope_and_permissions()
    {
        var (id, vake) = await OperatorWithApprovedApplicationAsync(null);
        using var _ = vake;
        await PostAsync(vake, $"/Applications/SignContract/{id}");
        await PostAsync(vake, $"/Applications/Disburse/{id}");
        await ProcessOutboxAsync();
        var loanId = await QueryAsync(db => db.Loans.Where(l => l.ApplicationId == new LoanApplicationId(id)).Select(l => l.Id.Value).SingleAsync(Ct));

        using var batumi = await SignInAsync(RoleCodes.Operator, BranchSeedCatalog.Batumi);
        Assert.Equal(HttpStatusCode.NotFound, (await batumi.GetAsync($"/Applications/Contract/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await batumi.GetAsync($"/Applications/Schedule/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await batumi.GetAsync($"/Loans/Details/{loanId}")).StatusCode);
        Assert.DoesNotContain(loanId.ToString(), await batumi.GetPageAsync("/Loans"), StringComparison.Ordinal);

        // Compliance holds neither contracts.manage nor loans.view.
        using var compliance = await SignInAsync(RoleCodes.Compliance);
        Assert.Equal("/Account/AccessDenied", (await compliance.GetAsync($"/Applications/Contract/{id}")).Headers.Location!.AbsolutePath);
        Assert.Equal("/Account/AccessDenied", (await compliance.GetAsync("/Loans")).Headers.Location!.AbsolutePath);

        // A draft has nothing to print yet.
        var draft = await CreateDraftAsync(vake);
        Assert.Equal(HttpStatusCode.NotFound, (await vake.GetAsync($"/Applications/Contract/{draft}")).StatusCode);

        // The client page lists the client's applications.
        var clientId = await QueryAsync(db => db.LoanApplications.Where(a => a.Id == new LoanApplicationId(id)).Select(a => a.BorrowerId.Value).SingleAsync(Ct));
        Assert.Contains($"data-application=\"{id}\"", await vake.GetPageAsync($"/Clients/Details/{clientId}"), StringComparison.Ordinal);
    }

    // ----- Helpers -----

    private async Task ProcessOutboxAsync() =>
        await Factory.Services.GetRequiredService<OutboxProcessor>().ProcessAllDueAsync(Ct); // the dispatcher is off in web tests

    private Task<ApplicationStatus> PageStatusAsync(Guid id) =>
        QueryAsync(db => db.LoanApplications.Where(a => a.Id == new LoanApplicationId(id)).Select(a => a.Status).SingleAsync(Ct));

    private static async Task AssertPdfAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.True(bytes.Length > 1_000);
        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }

    /// <summary>A Vake operator signed in, and an application of theirs approved by an underwriter (built in the domain).</summary>
    private async Task<(Guid Application, WebSession Browser)> OperatorWithApprovedApplicationAsync(string? personalNumber)
    {
        var (operatorName, operatorId, password) = await CreateUserAsync(RoleCodes.Operator, BranchSeedCatalog.Vake);
        var (_, underwriterId, _) = await CreateUserAsync(RoleCodes.SeniorUnderwriter);
        var lending = new TestLending(Factory.Services, Factory.Clock);
        var borrower = personalNumber is null ? (Domain.Clients.ClientId?)null : await lending.ClientAsync(personalNumber);
        var id = await lending.ApprovedAsync(LendingSeedCatalog.Consumer, 10_000m, operatorId, [underwriterId], borrower);
        var browser = NewSession();
        await browser.LoginAsync(operatorName, password);
        return (id.Value, browser);
    }
}
