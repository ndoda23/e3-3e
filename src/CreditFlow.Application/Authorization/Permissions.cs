namespace CreditFlow.Application.Authorization;

/// <summary>
/// Every permission code in CreditFlow. Code always checks these, never role names.
/// Metadata for the admin UI (module, name, description, read-only flag) lives in <see cref="PermissionCatalog"/>.
/// </summary>
public static class Permissions
{
    public static class Clients
    {
        public const string View = "clients.view";
        public const string Manage = "clients.manage";
    }

    public static class Applications
    {
        public const string View = "applications.view";
        public const string Create = "applications.create";
        public const string Cancel = "applications.cancel";
        public const string RecordClientResponse = "applications.record-client-response";
    }

    public static class Documents
    {
        public const string View = "documents.view";
        public const string Upload = "documents.upload";
    }

    public static class Compliance
    {
        public const string Review = "compliance.review";
    }

    public static class Underwriting
    {
        public const string Decide = "underwriting.decide";
        public const string DecideSenior = "underwriting.decide-senior";
        public const string CounterOffer = "underwriting.counter-offer";
    }

    public static class Scoring
    {
        public const string Override = "scoring.override";
    }

    public static class Committee
    {
        public const string Vote = "committee.vote";
    }

    public static class Contracts
    {
        public const string Manage = "contracts.manage";
    }

    public static class Loans
    {
        public const string View = "loans.view";
        public const string Disburse = "loans.disburse";
        public const string RecordRepayment = "loans.record-repayment";
    }

    public static class Reports
    {
        public const string View = "reports.view";
        public const string Export = "reports.export";
    }

    public static class Audit
    {
        public const string View = "audit.view";
    }

    public static class Admin
    {
        public const string UsersView = "admin.users.view";
        public const string UsersManage = "admin.users.manage";
        public const string RolesManage = "admin.roles.manage";
        public const string BranchesManage = "admin.branches.manage";
        public const string AuditView = "admin.audit.view";
        public const string ProductsManage = "admin.products.manage";
        public const string ScoringRulesManage = "admin.scoring-rules.manage";
        public const string ApprovalThresholdsManage = "admin.approval-thresholds.manage";
        public const string FxRatesManage = "admin.fx-rates.manage";
        public const string HolidaysManage = "admin.holidays.manage";
    }
}
