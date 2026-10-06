using Lams.Application.Authorization;
using Lams.Application.ReferenceData;

namespace Lams.Application.UnitTests.Authorization;

/// <summary>
/// Verifies every row of the seeded permission matrix for every role. The expected matrix below is written out
/// independently of <see cref="RoleSeedCatalog"/> (transcribed from the Phase 1 brief, FRD 5.2, 5.3 and 6.4).
/// </summary>
public sealed class PermissionMatrixTests
{
    private const string LO = RoleCodes.LoanOfficer;
    private const string UW = RoleCodes.Underwriter;
    private const string BM = RoleCodes.BranchManager;
    private const string BDM = RoleCodes.BusinessDevelopmentManager;
    private const string BO = RoleCodes.BackOffice;
    private const string AML = RoleCodes.AmlAnalyst;
    private const string FAB = RoleCodes.FinancialAnalysis;
    private const string HD = RoleCodes.Head;
    private const string DIR = RoleCodes.Director;
    private const string SYSADMIN = RoleCodes.SystemAdministrator;

    private static readonly string[] AllRoles = [LO, UW, BM, BDM, BO, AML, FAB, HD, DIR, SYSADMIN];
    private static readonly string[] BusinessRoles = [LO, UW, BM, BDM, BO, AML, FAB, HD, DIR];

    /// <summary>Permission → roles that hold it. Every catalog permission must appear here.</summary>
    private static readonly Dictionary<string, string[]> Expected = new()
    {
        ["applications.view-full"] = [LO, UW, BM, BDM, BO],
        ["aml.view-screening"] = [AML],
        ["pricing.view-exceptions"] = [FAB, HD, DIR],
        ["applications.create"] = [LO],
        ["applications.edit-terms"] = [LO, UW],
        ["applications.send-to-aml"] = [LO, UW, BM],
        ["applications.recommend"] = [LO],
        ["applications.decide"] = [UW, BM],
        ["applications.escalate"] = [UW, BM],
        ["applications.request-info"] = [LO, UW, BM],
        ["applications.refer-to-committee"] = [LO, UW, BM],
        ["applications.add-portfolio-note"] = [BDM],
        ["committee.vote"] = [UW, BM, HD, DIR],
        ["aml.record-outcome"] = [AML],
        ["pricing.confirm-roae"] = [FAB],
        ["pricing.approve-exception"] = [HD, DIR],
        ["backoffice.book-or-return"] = [BO],
        ["documents.view"] = [LO, UW, BM, BDM, BO, AML],
        ["documents.upload"] = [LO, UW, BM, BDM, BO, AML],
        ["documents.delete-own-new"] = [LO, UW, BM, BDM, BO, AML],
        ["documents.request-from-customer"] = [LO, UW, BM],
        ["reports.view"] = BusinessRoles,

        // Open questions 3 and 4: defined, granted to nobody.
        ["applications.reassign"] = [],
        ["applications.mark-priority"] = [],
        ["applications.tag"] = [],
        ["applications.add-internal-note"] = [],
        ["applications.repull-credit-data"] = [],
        ["applications.reverify-income"] = [],
        ["applications.print"] = [],
        ["applications.archive"] = [],

        // Administration: System Administrator only.
        ["admin.users.view"] = [SYSADMIN],
        ["admin.users.manage"] = [SYSADMIN],
        ["admin.roles.manage"] = [SYSADMIN],
        ["admin.authority-limits.manage"] = [SYSADMIN],
        ["admin.branches.manage"] = [SYSADMIN],
        ["admin.audit.view"] = [SYSADMIN],
        ["admin.credit-policy.manage"] = [SYSADMIN],
        ["admin.pricing.manage"] = [SYSADMIN],
        ["admin.settings.manage"] = [SYSADMIN],
    };

    public static TheoryData<string, string, bool> Cells()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var (permission, roles) in Expected)
        {
            foreach (var role in AllRoles)
            {
                data.Add(permission, role, roles.Contains(role));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Seeded_role_holds_exactly_the_expected_permissions(string permission, string roleCode, bool expected)
    {
        var granted = RoleSeedCatalog.Get(roleCode).Permissions.Contains(permission);

        Assert.Equal(expected, granted);
    }

    [Fact]
    public void Every_catalog_permission_is_covered_by_the_expected_matrix()
    {
        var catalog = PermissionCatalog.All.Select(p => p.Code).ToHashSet();

        Assert.Equal(catalog.Order(), Expected.Keys.Order());
    }

    [Fact]
    public void Seeded_roles_grant_only_catalog_permissions_without_duplicates()
    {
        foreach (var role in RoleSeedCatalog.All)
        {
            Assert.All(role.Permissions, p => Assert.True(PermissionCatalog.Contains(p), $"{role.Code}: {p}"));
            Assert.Equal(role.Permissions.Count, role.Permissions.Distinct().Count());
        }
    }

    [Fact]
    public void Open_question_permissions_are_defined_but_granted_to_no_role()
    {
        string[] openQuestion =
        [
            Permissions.Applications.Reassign, Permissions.Applications.MarkPriority, Permissions.Applications.Tag,
            Permissions.Applications.AddInternalNote, Permissions.Applications.RepullCreditData,
            Permissions.Applications.ReverifyIncome, Permissions.Applications.Print, Permissions.Applications.Archive,
        ];

        Assert.All(openQuestion, p => Assert.True(PermissionCatalog.Contains(p)));
        Assert.DoesNotContain(RoleSeedCatalog.All.SelectMany(r => r.Permissions), openQuestion.Contains);
    }

    [Fact]
    public void Head_and_Director_never_take_the_single_loan_credit_decision()
    {
        // FRD 5.3 wins over the prototype (which sets canDecide = true for them).
        Assert.DoesNotContain(Permissions.Applications.Decide, RoleSeedCatalog.Get(HD).Permissions);
        Assert.DoesNotContain(Permissions.Applications.Decide, RoleSeedCatalog.Get(DIR).Permissions);
    }

    [Fact]
    public void Business_development_manager_has_no_write_permission_apart_from_notes_and_documents()
    {
        string[] allowed =
        [
            Permissions.Applications.ViewFull, Permissions.Applications.AddPortfolioNote,
            Permissions.Documents.View, Permissions.Documents.Upload, Permissions.Documents.DeleteOwnNew,
            Permissions.Reports.View,
        ];

        Assert.Equal(allowed.Order(), RoleSeedCatalog.Get(BDM).Permissions.Order());
    }
}
