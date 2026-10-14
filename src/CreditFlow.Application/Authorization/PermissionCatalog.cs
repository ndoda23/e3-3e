using System.Collections.Frozen;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;

namespace CreditFlow.Application.Authorization;

/// <summary>A group of permissions shown together in the role permission editor.</summary>
public sealed record PermissionModule(string Key, string DisplayName, int Order);

/// <summary>A permission with the texts shown in the admin UI (English fallback; the UI localizes by code).</summary>
/// <param name="Code">The permission code.</param>
/// <param name="Module">The editor group.</param>
/// <param name="DisplayName">Short name.</param>
/// <param name="Description">What the permission allows.</param>
/// <param name="IsReadOnly">The permission only reads data, so a read-only role (Auditor) may hold it.</param>
public sealed record PermissionDefinition(
    string Code, PermissionModule Module, string DisplayName, string Description, bool IsReadOnly)
{
    public bool IsAdministrative => Code.StartsWith(PermissionCode.AdministrativePrefix, StringComparison.Ordinal);
}

/// <summary>The single catalog of permissions known to CreditFlow.</summary>
public static class PermissionCatalog
{
    public static readonly PermissionModule ClientsModule = new("clients", "Clients", 1);
    public static readonly PermissionModule ApplicationsModule = new("applications", "Applications", 2);
    public static readonly PermissionModule DocumentsModule = new("documents", "Documents", 3);
    public static readonly PermissionModule ComplianceModule = new("compliance", "Compliance", 4);
    public static readonly PermissionModule UnderwritingModule = new("underwriting", "Underwriting", 5);
    public static readonly PermissionModule ScoringModule = new("scoring", "Scoring", 6);
    public static readonly PermissionModule CommitteeModule = new("committee", "Credit committee", 7);
    public static readonly PermissionModule ContractsModule = new("contracts", "Contracts", 8);
    public static readonly PermissionModule LoansModule = new("loans", "Loans", 9);
    public static readonly PermissionModule ReportsModule = new("reports", "Reports", 10);
    public static readonly PermissionModule AuditModule = new("audit", "Audit", 11);
    public static readonly PermissionModule AdministrationModule = new("admin", "Administration", 12);

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        // Clients
        Read(Permissions.Clients.View, ClientsModule, "View clients",
            "See client records: identity, ID document, incomes, obligations and screening results."),
        Write(Permissions.Clients.Manage, ClientsModule, "Manage clients",
            "Register clients and edit their identity document, incomes and obligations."),

        // Applications
        Read(Permissions.Applications.View, ApplicationsModule, "View applications",
            "Open applications within the role's data scope, with status history and scoring results."),
        Write(Permissions.Applications.Create, ApplicationsModule, "Create applications",
            "Create and edit draft applications (parties, terms, collateral) and submit them."),
        Write(Permissions.Applications.Cancel, ApplicationsModule, "Cancel applications",
            "Cancel an application before disbursement, with a reason."),
        Write(Permissions.Applications.RecordClientResponse, ApplicationsModule, "Record client response",
            "Record that the client accepted or declined a counter-offer."),

        // Documents
        Read(Permissions.Documents.View, DocumentsModule, "View documents",
            "View and download the documents of an application."),
        Write(Permissions.Documents.Upload, DocumentsModule, "Upload documents",
            "Upload documents to an application and remove them before scoring."),

        // Compliance
        Write(Permissions.Compliance.Review, ComplianceModule, "Compliance review",
            "Clear or reject an application held for a sanctions or PEP match, with a reason."),

        // Underwriting
        Write(Permissions.Underwriting.Decide, UnderwritingModule, "Decide (Underwriter)",
            "Approve or reject applications up to the Underwriter threshold."),
        Write(Permissions.Underwriting.DecideSenior, UnderwritingModule, "Decide (Senior Underwriter)",
            "Approve or reject applications up to the Senior Underwriter threshold, including escalated ones."),
        Write(Permissions.Underwriting.CounterOffer, UnderwritingModule, "Counter-offer",
            "Offer a different amount, term or rate; the client must agree and the application is scored again."),

        // Scoring
        Write(Permissions.Scoring.Override, ScoringModule, "Override scoring rule",
            "Approve despite a failed overridable scoring rule, with a reason."),

        // Committee
        Write(Permissions.Committee.Vote, CommitteeModule, "Committee vote",
            "Vote on applications above the Senior Underwriter threshold."),

        // Contracts
        Write(Permissions.Contracts.Manage, ContractsModule, "Manage contracts",
            "Mark the contract signed and the conditions met; print the contract and the schedule."),

        // Loans
        Write(Permissions.Loans.Disburse, LoansModule, "Disburse",
            "Request the disbursement of an approved loan whose contract is signed and conditions are met."),
        Read(Permissions.Loans.View, LoansModule, "View loans", "See loans, their schedules and repayments."),
        Write(Permissions.Loans.RecordRepayment, LoansModule, "Record repayments",
            "Record regular and early repayments."),

        // Reports
        Read(Permissions.Reports.View, ReportsModule, "View reports", "Run reports, limited to the role's data scope."),
        Read(Permissions.Reports.Export, ReportsModule, "Export reports", "Export reports to Excel and CSV."),

        // Audit
        Read(Permissions.Audit.View, AuditModule, "View audit log", "Read the audit log (read-only roles)."),

        // Administration (Admin only)
        Read(Permissions.Admin.UsersView, AdministrationModule, "View users", "See the user list and user details."),
        Write(Permissions.Admin.UsersManage, AdministrationModule, "Manage users",
            "Create and edit users, activate / deactivate, unlock, reset passwords, force sign-out, require MFA."),
        Write(Permissions.Admin.RolesManage, AdministrationModule, "Manage roles",
            "Change the permissions of roles and require MFA per role."),
        Write(Permissions.Admin.BranchesManage, AdministrationModule, "Manage branches",
            "Create, edit, activate and deactivate branches."),
        Read(Permissions.Admin.AuditView, AdministrationModule, "View audit log", "Read the audit log."),
        Write(Permissions.Admin.ProductsManage, AdministrationModule, "Manage loan products",
            "Create loan products and publish new product versions."),
        Write(Permissions.Admin.ScoringRulesManage, AdministrationModule, "Manage scoring rules",
            "Publish new versions of the scoring rule set."),
        Write(Permissions.Admin.ApprovalThresholdsManage, AdministrationModule, "Manage approval thresholds",
            "Change the Underwriter and Senior Underwriter thresholds and the committee quorum."),
        Write(Permissions.Admin.FxRatesManage, AdministrationModule, "Manage FX rates",
            "Edit and import exchange rates."),
        Write(Permissions.Admin.HolidaysManage, AdministrationModule, "Manage holidays",
            "Edit the bank holidays used by the working-day calendar."),
    ];

    public static IReadOnlyList<PermissionModule> Modules { get; } =
        All.Select(p => p.Module).Distinct().OrderBy(m => m.Order).ToList();

    private static readonly FrozenDictionary<string, PermissionDefinition> ByCode =
        All.ToFrozenDictionary(p => p.Code, StringComparer.Ordinal);

    public static bool Contains(string code) => ByCode.ContainsKey(code);

    public static PermissionDefinition? Find(string code) => ByCode.GetValueOrDefault(code);

    /// <summary>True for a known permission that only reads data. Unknown codes are not read-only (fail closed).</summary>
    public static bool IsReadOnly(PermissionCode code) => Find(code.Value) is { IsReadOnly: true };

    /// <summary>Builds a role's permission set with the segregation-of-duties rules of its kind.</summary>
    public static Result<PermissionSet> CreateSet(RoleKind kind, IEnumerable<string> permissionCodes) =>
        PermissionSet.Create(kind, permissionCodes, IsReadOnly);

    private static PermissionDefinition Read(string code, PermissionModule module, string displayName, string description) =>
        new(code, module, displayName, description, IsReadOnly: true);

    private static PermissionDefinition Write(string code, PermissionModule module, string displayName, string description) =>
        new(code, module, displayName, description, IsReadOnly: false);
}
