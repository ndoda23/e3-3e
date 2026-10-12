using System.Collections.Frozen;
using CreditFlow.Domain.Roles;

namespace CreditFlow.Application.Authorization;

/// <summary>A group of permissions shown together in the role permission editor.</summary>
public sealed record PermissionModule(string Key, string DisplayName, int Order);

/// <summary>A permission with the texts shown in the admin UI (English fallback; the UI localizes by code).</summary>
public sealed record PermissionDefinition(string Code, PermissionModule Module, string DisplayName, string Description)
{
    public bool IsAdministrative => Code.StartsWith(PermissionCode.AdministrativePrefix, StringComparison.Ordinal);
}

/// <summary>The single catalog of permissions known to CreditFlow.</summary>
public static class PermissionCatalog
{
    public static readonly PermissionModule ApplicationsModule = new("applications", "Applications", 1);
    public static readonly PermissionModule AmlModule = new("aml", "AML / KYC", 2);
    public static readonly PermissionModule PricingModule = new("pricing", "Pricing exceptions", 3);
    public static readonly PermissionModule CommitteeModule = new("committee", "Credit committee", 4);
    public static readonly PermissionModule BackOfficeModule = new("backoffice", "Back office", 5);
    public static readonly PermissionModule DocumentsModule = new("documents", "Documents", 6);
    public static readonly PermissionModule ReportsModule = new("reports", "Reports", 7);
    public static readonly PermissionModule AdministrationModule = new("admin", "Administration", 8);

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        // Applications
        Define(Permissions.Applications.ViewFull, ApplicationsModule, "View full application file",
            "Open the complete application file (all tabs), within the role's data scope."),
        Define(Permissions.Applications.Create, ApplicationsModule, "Create applications",
            "Create applications and customer packages."),
        Define(Permissions.Applications.EditTerms, ApplicationsModule, "Revise terms",
            "Revise loan terms and the repayment schedule."),
        Define(Permissions.Applications.SendToAml, ApplicationsModule, "Send for AML check",
            "Send the file to the AML / KYC screening queue."),
        Define(Permissions.Applications.Recommend, ApplicationsModule, "Recommend",
            "Recommend approve or decline and forward the file to an underwriter."),
        Define(Permissions.Applications.Decide, ApplicationsModule, "Approve / decline",
            "Approve or decline a loan within the user's credit decision limit."),
        Define(Permissions.Applications.Escalate, ApplicationsModule, "Escalate",
            "Escalate a file that is above the user's authority limit."),
        Define(Permissions.Applications.RequestInfo, ApplicationsModule, "Request more information",
            "Ask the customer for more documents or information."),
        Define(Permissions.Applications.ReferToCommittee, ApplicationsModule, "Refer to committee",
            "Refer a file or package to the credit committee."),
        Define(Permissions.Applications.AddPortfolioNote, ApplicationsModule, "Add portfolio note",
            "Add a portfolio note to a file (read-only roles)."),
        Define(Permissions.Applications.Reassign, ApplicationsModule, "Reassign",
            "Reassign a file to another user (actions menu). No role defined yet."),
        Define(Permissions.Applications.MarkPriority, ApplicationsModule, "Mark priority",
            "Mark a file as priority (actions menu). No role defined yet."),
        Define(Permissions.Applications.Tag, ApplicationsModule, "Tag",
            "Tag a file (actions menu). No role defined yet."),
        Define(Permissions.Applications.AddInternalNote, ApplicationsModule, "Internal note",
            "Add an internal note (actions menu). No role defined yet."),
        Define(Permissions.Applications.RepullCreditData, ApplicationsModule, "Re-pull credit data",
            "Pull the credit bureau report again (actions menu). No role defined yet."),
        Define(Permissions.Applications.ReverifyIncome, ApplicationsModule, "Re-verify income",
            "Verify RS.ge income again (actions menu). No role defined yet."),
        Define(Permissions.Applications.Print, ApplicationsModule, "Print application",
            "Print the application (actions menu). No role defined yet."),
        Define(Permissions.Applications.Archive, ApplicationsModule, "Archive",
            "Archive an application (actions menu). No role defined yet."),

        // AML / KYC
        Define(Permissions.Aml.ViewScreening, AmlModule, "View AML screening file",
            "Restricted view of files sent for AML: identity, related parties and documents only."),
        Define(Permissions.Aml.RecordOutcome, AmlModule, "Record AML outcome",
            "Clear, flag or request more information on an AML check."),

        // Pricing exceptions
        Define(Permissions.Pricing.ViewExceptions, PricingModule, "View pricing exceptions",
            "Restricted view of pricing exceptions: ROAE breakdown and offered terms only."),
        Define(Permissions.Pricing.ConfirmRoae, PricingModule, "Confirm / override ROAE",
            "Confirm or override the customer ROAE with a note and forward the exception."),
        Define(Permissions.Pricing.ApproveException, PricingModule, "Approve pricing exception",
            "Approve or decline a pricing exception within the user's pricing exception limit."),

        // Committee
        Define(Permissions.Committee.Vote, CommitteeModule, "Record committee verdicts",
            "Vote on and record package committee verdicts."),

        // Back office
        Define(Permissions.BackOffice.BookOrReturn, BackOfficeModule, "Book or return",
            "Book an approved loan or return it to the loan officer with a reason."),

        // Documents
        Define(Permissions.Documents.View, DocumentsModule, "View and download documents",
            "View and download the documents of a file."),
        Define(Permissions.Documents.Upload, DocumentsModule, "Upload documents",
            "Add documents to a file."),
        Define(Permissions.Documents.DeleteOwnNew, DocumentsModule, "Remove own new documents",
            "Remove a document the user just added. Documents that came with the application can never be removed."),
        Define(Permissions.Documents.RequestFromCustomer, DocumentsModule, "Request documents from customer",
            "Request documents from the customer by email or SMS."),

        // Reports
        Define(Permissions.Reports.View, ReportsModule, "View reports",
            "Run reports, limited to the role's data scope."),

        // Administration (System Administrator only)
        Define(Permissions.Admin.UsersView, AdministrationModule, "View users", "See the user list and user details."),
        Define(Permissions.Admin.UsersManage, AdministrationModule, "Manage users",
            "Create and edit users, activate / deactivate, unlock, reset passwords, force sign-out, require MFA."),
        Define(Permissions.Admin.RolesManage, AdministrationModule, "Manage roles",
            "Change the permissions of roles and require MFA per role."),
        Define(Permissions.Admin.AuthorityLimitsManage, AdministrationModule, "Manage authority limits",
            "Edit credit decision and pricing exception limits."),
        Define(Permissions.Admin.BranchesManage, AdministrationModule, "Manage branches",
            "Create, edit, activate and deactivate branches."),
        Define(Permissions.Admin.AuditView, AdministrationModule, "View audit log", "Read the audit log."),
        Define(Permissions.Admin.CreditPolicyManage, AdministrationModule, "Manage credit policy",
            "Edit the credit policy."),
        Define(Permissions.Admin.PricingManage, AdministrationModule, "Manage risk-based pricing",
            "Manage the risk-based pricing grid."),
        Define(Permissions.Admin.SettingsManage, AdministrationModule, "Manage settings",
            "Manage system settings."),
    ];

    public static IReadOnlyList<PermissionModule> Modules { get; } =
        All.Select(p => p.Module).Distinct().OrderBy(m => m.Order).ToList();

    private static readonly FrozenDictionary<string, PermissionDefinition> ByCode =
        All.ToFrozenDictionary(p => p.Code, StringComparer.Ordinal);

    public static bool Contains(string code) => ByCode.ContainsKey(code);

    public static PermissionDefinition? Find(string code) => ByCode.GetValueOrDefault(code);

    private static PermissionDefinition Define(
        string code, PermissionModule module, string displayName, string description) =>
        new(code, module, displayName, description);
}
