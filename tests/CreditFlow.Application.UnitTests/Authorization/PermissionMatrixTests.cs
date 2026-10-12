using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;

namespace CreditFlow.Application.UnitTests.Authorization;

/// <summary>
/// Verifies every cell of the seeded permission matrix for every role. The expected matrix below is written out
/// independently of <see cref="RoleSeedCatalog"/> (transcribed from PLAN.md §P.2.1, the approved role matrix).
/// </summary>
public sealed class PermissionMatrixTests
{
    private const string OP = RoleCodes.Operator;
    private const string UW = RoleCodes.Underwriter;
    private const string SUW = RoleCodes.SeniorUnderwriter;
    private const string CC = RoleCodes.CreditCommittee;
    private const string COMP = RoleCodes.Compliance;
    private const string AUD = RoleCodes.Auditor;
    private const string ADMIN = RoleCodes.Admin;

    private static readonly string[] AllRoles = [OP, UW, SUW, CC, COMP, AUD, ADMIN];

    /// <summary>Permission → roles that hold it. Every catalog permission must appear here.</summary>
    private static readonly Dictionary<string, string[]> Expected = new()
    {
        ["clients.view"] = [OP, UW, SUW, CC, COMP, AUD],
        ["clients.manage"] = [OP],
        ["applications.view"] = [OP, UW, SUW, CC, COMP, AUD],
        ["applications.create"] = [OP],
        ["applications.cancel"] = [OP],
        ["applications.record-client-response"] = [OP],
        ["documents.view"] = [OP, UW, SUW, CC, COMP, AUD],
        ["documents.upload"] = [OP],
        ["compliance.review"] = [COMP],
        ["underwriting.decide"] = [UW, SUW],
        ["underwriting.decide-senior"] = [SUW],
        ["underwriting.counter-offer"] = [UW, SUW],
        ["scoring.override"] = [SUW],
        ["committee.vote"] = [CC],
        ["contracts.manage"] = [OP],
        ["loans.view"] = [OP, UW, SUW, CC, AUD],
        ["loans.disburse"] = [OP],
        ["loans.record-repayment"] = [OP],
        ["reports.view"] = [OP, UW, SUW, CC, AUD],
        ["reports.export"] = [SUW, AUD],
        ["audit.view"] = [AUD],

        // Administration: Admin only.
        ["admin.users.view"] = [ADMIN],
        ["admin.users.manage"] = [ADMIN],
        ["admin.roles.manage"] = [ADMIN],
        ["admin.branches.manage"] = [ADMIN],
        ["admin.audit.view"] = [ADMIN],
        ["admin.products.manage"] = [ADMIN],
        ["admin.scoring-rules.manage"] = [ADMIN],
        ["admin.approval-thresholds.manage"] = [ADMIN],
        ["admin.fx-rates.manage"] = [ADMIN],
        ["admin.holidays.manage"] = [ADMIN],
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
    public void No_role_that_creates_applications_also_decides_them()
    {
        // Four-eyes starts with the matrix: nobody who decides (underwriters, committee, compliance) creates.
        string[] deciding =
        [
            Permissions.Underwriting.Decide, Permissions.Underwriting.DecideSenior, Permissions.Committee.Vote,
            Permissions.Compliance.Review,
        ];

        foreach (var role in RoleSeedCatalog.All.Where(r => r.Permissions.Contains(Permissions.Applications.Create)))
        {
            Assert.DoesNotContain(role.Permissions, deciding.Contains);
        }
    }

    [Fact]
    public void Auditor_holds_every_non_administrative_read_permission_and_nothing_else()
    {
        var expected = PermissionCatalog.All.Where(p => p.IsReadOnly && !p.IsAdministrative).Select(p => p.Code);

        Assert.Equal(expected.Order(), RoleSeedCatalog.Get(AUD).Permissions.Order());
    }
}
