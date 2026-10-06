using Lams.Application.Authorization;
using Lams.Domain.Roles;
using P = Lams.Application.Authorization.Permissions;

namespace Lams.Application.ReferenceData;

/// <summary>Definition of a seeded role and its initial permissions.</summary>
public sealed record RoleSeed
{
    public required string Code { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required DataScope DataScope { get; init; }

    public required DashboardKind Dashboard { get; init; }

    public required AttributeRule BranchRule { get; init; }

    public required AttributeRule SegmentRule { get; init; }

    public required AttributeRule AuthorityLevelRule { get; init; }

    public bool IsAdministrative { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }
}

/// <summary>
/// The 9 business roles (FRD 5.1) and the System Administrator, with the permission matrix from FRD 5.2, 5.3 and 6.4.
/// Seeded idempotently: a role is created with these permissions only if it does not exist yet; afterwards the
/// administrator owns the permissions (role permission editor).
/// </summary>
public static class RoleSeedCatalog
{
    public static IReadOnlyList<RoleSeed> All { get; } =
    [
        new()
        {
            Code = RoleCodes.LoanOfficer,
            DisplayName = "Loan Officer",
            Description = "Creates applications, completes the file and recommends. Sees own applications.",
            DataScope = DataScope.Own,
            Dashboard = DashboardKind.Officer,
            BranchRule = AttributeRule.Required,
            SegmentRule = AttributeRule.NotAllowed,
            AuthorityLevelRule = AttributeRule.Required,
            Permissions =
            [
                P.Applications.ViewFull, P.Applications.Create, P.Applications.EditTerms, P.Applications.SendToAml,
                P.Applications.Recommend, P.Applications.RequestInfo, P.Applications.ReferToCommittee,
                .. FullDocumentAccess, P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.Underwriter,
            DisplayName = "Underwriter",
            Description = "Decides within the personal credit limit, escalates above it. Sees assigned applications.",
            DataScope = DataScope.Assigned,
            Dashboard = DashboardKind.Underwriter,
            BranchRule = AttributeRule.Required,
            SegmentRule = AttributeRule.NotAllowed,
            AuthorityLevelRule = AttributeRule.Required,
            Permissions =
            [
                P.Applications.ViewFull, P.Applications.EditTerms, P.Applications.SendToAml, P.Applications.Decide,
                P.Applications.Escalate, P.Applications.RequestInfo, P.Applications.ReferToCommittee,
                P.Committee.Vote, .. FullDocumentAccess, P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.BranchManager,
            DisplayName = "Branch Manager",
            Description = "Decides within the branch limit and refers to the committee. Sees the whole branch.",
            DataScope = DataScope.Branch,
            Dashboard = DashboardKind.Branch,
            BranchRule = AttributeRule.Required,
            SegmentRule = AttributeRule.NotAllowed,
            AuthorityLevelRule = AttributeRule.NotAllowed,
            Permissions =
            [
                P.Applications.ViewFull, P.Applications.SendToAml, P.Applications.Decide, P.Applications.Escalate,
                P.Applications.RequestInfo, P.Applications.ReferToCommittee, P.Committee.Vote,
                .. FullDocumentAccess, P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.BusinessDevelopmentManager,
            DisplayName = "Business Development Manager",
            Description = "Read-only view of every file; adds portfolio notes and runs reports.",
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.BusinessDevelopment,
            BranchRule = AttributeRule.Optional,
            SegmentRule = AttributeRule.NotAllowed,
            AuthorityLevelRule = AttributeRule.NotAllowed,
            Permissions =
            [
                P.Applications.ViewFull, P.Applications.AddPortfolioNote,

                // Open question 1: FRD 5.3 says read-only, FRD 6.4 lets BDM upload and remove own new documents.
                // Seeded as FRD 6.4 says; changeable in the role permission editor.
                P.Documents.View, P.Documents.Upload, P.Documents.DeleteOwnNew,
                P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.BackOffice,
            DisplayName = "Back Office",
            Description = "Verifies approved terms, then books the loan or returns it to the officer.",

            // Open question 2: FRD 5.1 "whole bank" vs 5.3 "approved applications waiting for booking".
            // Seeded as Organization; queue filtering comes in a later phase.
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.BackOffice,
            BranchRule = AttributeRule.Optional,
            SegmentRule = AttributeRule.NotAllowed,
            AuthorityLevelRule = AttributeRule.NotAllowed,
            Permissions =
            [
                P.Applications.ViewFull, P.BackOffice.BookOrReturn,
                P.Documents.View, P.Documents.Upload, P.Documents.DeleteOwnNew,
                P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.AmlAnalyst,
            DisplayName = "AML / KYC Analyst",
            Description = "Screens files sent for AML: identity, related parties and documents only.",
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.AmlScreening,
            BranchRule = AttributeRule.Optional,
            SegmentRule = AttributeRule.NotAllowed,
            AuthorityLevelRule = AttributeRule.NotAllowed,
            Permissions =
            [
                P.Aml.ViewScreening, P.Aml.RecordOutcome,
                P.Documents.View, P.Documents.Upload, P.Documents.DeleteOwnNew,
                P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.FinancialAnalysis,
            DisplayName = "Financial Analysis & Budgeting",
            Description = "Confirms or overrides the customer ROAE on pricing exceptions.",
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.Fab,
            BranchRule = AttributeRule.Optional,
            SegmentRule = AttributeRule.NotAllowed,
            AuthorityLevelRule = AttributeRule.NotAllowed,
            Permissions = [P.Pricing.ViewExceptions, P.Pricing.ConfirmRoae, P.Reports.View],
        },
        new()
        {
            Code = RoleCodes.Head,
            DisplayName = "Head (Retail / Business)",
            Description = "Approves pricing exceptions of their segment within limit; votes on package committee.",
            DataScope = DataScope.Segment,
            Dashboard = DashboardKind.Exceptions,
            BranchRule = AttributeRule.Optional,
            SegmentRule = AttributeRule.Required,
            AuthorityLevelRule = AttributeRule.NotAllowed,
            Permissions =
            [
                P.Pricing.ViewExceptions, P.Pricing.ApproveException, P.Committee.Vote, P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.Director,
            DisplayName = "Bank Director",
            Description = "Approves escalated pricing exceptions with no limit; votes on package committee.",
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.Exceptions,
            BranchRule = AttributeRule.Optional,
            SegmentRule = AttributeRule.NotAllowed,
            AuthorityLevelRule = AttributeRule.NotAllowed,
            Permissions =
            [
                P.Pricing.ViewExceptions, P.Pricing.ApproveException, P.Committee.Vote, P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.SystemAdministrator,
            DisplayName = "System Administrator",
            Description = "Administers users, roles, limits and branches. No access to loan data.",
            DataScope = DataScope.None,
            Dashboard = DashboardKind.Administration,
            BranchRule = AttributeRule.NotAllowed,
            SegmentRule = AttributeRule.NotAllowed,
            AuthorityLevelRule = AttributeRule.NotAllowed,
            IsAdministrative = true,
            Permissions = PermissionCatalog.All.Where(p => p.IsAdministrative).Select(p => p.Code).ToList(),
        },
    ];

    public static RoleSeed Get(string code) => All.Single(r => r.Code == code);

    private static string[] FullDocumentAccess =>
    [
        P.Documents.View, P.Documents.Upload, P.Documents.DeleteOwnNew, P.Documents.RequestFromCustomer,
    ];
}
