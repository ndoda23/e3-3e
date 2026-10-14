using CreditFlow.Application.Authorization;
using CreditFlow.Domain.Roles;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Application.ReferenceData;

/// <summary>Definition of a seeded role and its initial permissions.</summary>
public sealed record RoleSeed
{
    public required string Code { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required RoleKind Kind { get; init; }

    public required DataScope DataScope { get; init; }

    public required DashboardKind Dashboard { get; init; }

    public required AttributeRule BranchRule { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }
}

/// <summary>
/// The roles of the CreditFlow task with their initial permission matrix. The definition (name, kind, scope,
/// dashboard, branch rule) is code-owned; the permissions are seeded once and then belong to the administrator
/// (role permission editor).
/// </summary>
public static class RoleSeedCatalog
{
    public static IReadOnlyList<RoleSeed> All { get; } =
    [
        new()
        {
            Code = RoleCodes.Operator,
            DisplayName = "Operator",
            Description = "Registers clients, creates and submits applications, signs contracts and disburses. "
                          + "Sees the applications of their branch.",
            Kind = RoleKind.Business,
            DataScope = DataScope.Branch,
            Dashboard = DashboardKind.Operator,
            BranchRule = AttributeRule.Required,
            Permissions =
            [
                P.Clients.View, P.Clients.Manage,
                P.Applications.View, P.Applications.Create, P.Applications.Cancel,
                P.Applications.RecordClientResponse,
                P.Documents.View, P.Documents.Upload,
                P.Contracts.Manage, P.Loans.View, P.Loans.Disburse, P.Loans.RecordRepayment,
                P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.Underwriter,
            DisplayName = "Underwriter",
            Description = "Decides applications up to the Underwriter threshold and makes counter-offers.",
            Kind = RoleKind.Business,
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.Underwriting,
            BranchRule = AttributeRule.NotAllowed,
            Permissions =
            [
                P.Clients.View, P.Applications.View, P.Documents.View,
                P.Underwriting.Decide, P.Underwriting.CounterOffer,
                P.Loans.View, P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.SeniorUnderwriter,
            DisplayName = "Senior Underwriter",
            Description = "Decides applications up to the Senior Underwriter threshold, including escalated ones; "
                          + "may override overridable scoring rules.",
            Kind = RoleKind.Business,
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.Underwriting,
            BranchRule = AttributeRule.NotAllowed,
            Permissions =
            [
                P.Clients.View, P.Applications.View, P.Documents.View,
                P.Underwriting.Decide, P.Underwriting.DecideSenior, P.Underwriting.CounterOffer, P.Scoring.Override,
                P.Loans.View, P.Reports.View, P.Reports.Export,
            ],
        },
        new()
        {
            Code = RoleCodes.CreditCommittee,
            DisplayName = "Credit Committee",
            Description = "Votes on applications above the Senior Underwriter threshold.",
            Kind = RoleKind.Business,
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.Committee,
            BranchRule = AttributeRule.NotAllowed,
            Permissions =
            [
                P.Clients.View, P.Applications.View, P.Documents.View, P.Committee.Vote,
                P.Loans.View, P.Reports.View,
            ],
        },
        new()
        {
            Code = RoleCodes.Compliance,
            DisplayName = "Compliance",
            Description = "Clears or rejects applications held for a sanctions or PEP match.",
            Kind = RoleKind.Business,
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.Compliance,
            BranchRule = AttributeRule.NotAllowed,
            Permissions = [P.Clients.View, P.Applications.View, P.Documents.View, P.Compliance.Review],
        },
        new()
        {
            Code = RoleCodes.Auditor,
            DisplayName = "Auditor",
            Description = "Reads everything, changes nothing.",
            Kind = RoleKind.ReadOnly,
            DataScope = DataScope.Organization,
            Dashboard = DashboardKind.Audit,
            BranchRule = AttributeRule.NotAllowed,
            Permissions = PermissionCatalog.All
                .Where(p => p.IsReadOnly && !p.IsAdministrative)
                .Select(p => p.Code)
                .ToList(),
        },
        new()
        {
            Code = RoleCodes.Admin,
            DisplayName = "Admin",
            Description = "Administers users, roles, branches and reference data. No access to loan data.",
            Kind = RoleKind.Administrative,
            DataScope = DataScope.None,
            Dashboard = DashboardKind.Administration,
            BranchRule = AttributeRule.NotAllowed,
            Permissions = PermissionCatalog.All.Where(p => p.IsAdministrative).Select(p => p.Code).ToList(),
        },
    ];

    public static RoleSeed Get(string code) => All.Single(r => r.Code == code);
}
