namespace CreditFlow.Application.Authorization;

/// <summary>
/// Every permission code in CreditFlow. Code always checks these, never role names.
/// Metadata for the admin UI (module, name, description) lives in <see cref="PermissionCatalog"/>.
/// </summary>
public static class Permissions
{
    public static class Applications
    {
        public const string ViewFull = "applications.view-full";
        public const string Create = "applications.create";
        public const string EditTerms = "applications.edit-terms";
        public const string SendToAml = "applications.send-to-aml";
        public const string Recommend = "applications.recommend";
        public const string Decide = "applications.decide";
        public const string Escalate = "applications.escalate";
        public const string RequestInfo = "applications.request-info";
        public const string ReferToCommittee = "applications.refer-to-committee";
        public const string AddPortfolioNote = "applications.add-portfolio-note";

        // Actions menu (FRD M4). No role is defined for these yet (open questions 3 and 4): granted to nobody.
        public const string Reassign = "applications.reassign";
        public const string MarkPriority = "applications.mark-priority";
        public const string Tag = "applications.tag";
        public const string AddInternalNote = "applications.add-internal-note";
        public const string RepullCreditData = "applications.repull-credit-data";
        public const string ReverifyIncome = "applications.reverify-income";
        public const string Print = "applications.print";
        public const string Archive = "applications.archive";
    }

    public static class Aml
    {
        public const string ViewScreening = "aml.view-screening";
        public const string RecordOutcome = "aml.record-outcome";
    }

    public static class Pricing
    {
        public const string ViewExceptions = "pricing.view-exceptions";
        public const string ConfirmRoae = "pricing.confirm-roae";
        public const string ApproveException = "pricing.approve-exception";
    }

    public static class Committee
    {
        public const string Vote = "committee.vote";
    }

    public static class BackOffice
    {
        public const string BookOrReturn = "backoffice.book-or-return";
    }

    public static class Documents
    {
        public const string View = "documents.view";
        public const string Upload = "documents.upload";
        public const string DeleteOwnNew = "documents.delete-own-new";
        public const string RequestFromCustomer = "documents.request-from-customer";
    }

    public static class Reports
    {
        public const string View = "reports.view";
    }

    public static class Admin
    {
        public const string UsersView = "admin.users.view";
        public const string UsersManage = "admin.users.manage";
        public const string RolesManage = "admin.roles.manage";
        public const string AuthorityLimitsManage = "admin.authority-limits.manage";
        public const string BranchesManage = "admin.branches.manage";
        public const string AuditView = "admin.audit.view";
        public const string CreditPolicyManage = "admin.credit-policy.manage";
        public const string PricingManage = "admin.pricing.manage";
        public const string SettingsManage = "admin.settings.manage";
    }
}
