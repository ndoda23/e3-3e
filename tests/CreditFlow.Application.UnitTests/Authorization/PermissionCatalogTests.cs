using System.Reflection;
using CreditFlow.Application.Authorization;
using CreditFlow.Domain.Roles;

namespace CreditFlow.Application.UnitTests.Authorization;

public sealed class PermissionCatalogTests
{
    [Fact]
    public void Codes_are_unique_and_well_formed()
    {
        var codes = PermissionCatalog.All.Select(p => p.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.True(PermissionCode.Create(code).IsSuccess, code));
    }

    [Fact]
    public void Every_constant_in_Permissions_is_in_the_catalog_and_vice_versa()
    {
        var constants = typeof(Permissions)
            .GetNestedTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(constants, PermissionCatalog.All.Select(p => p.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_permission_has_a_module_name_and_description()
    {
        Assert.All(PermissionCatalog.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
            Assert.StartsWith(p.Module.Key + ".", p.Code, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void There_are_ten_administration_permissions()
    {
        Assert.Equal(10, PermissionCatalog.All.Count(p => p.IsAdministrative));
    }

    [Fact]
    public void Lookup_by_code()
    {
        Assert.True(PermissionCatalog.Contains(Permissions.Underwriting.Decide));
        Assert.False(PermissionCatalog.Contains("applications.unknown"));
        Assert.Equal(PermissionCatalog.UnderwritingModule, PermissionCatalog.Find(Permissions.Underwriting.Decide)!.Module);
    }

    [Fact]
    public void Read_only_permissions_are_exactly_the_ones_that_only_view_or_export()
    {
        // Written out: a permission marked read-only by mistake would let the Auditor change data.
        string[] expected =
        [
            "admin.audit.view", "admin.users.view", "applications.view", "audit.view", "clients.view",
            "documents.view", "loans.view", "reports.export", "reports.view",
        ];

        Assert.Equal(
            expected,
            PermissionCatalog.All.Where(p => p.IsReadOnly).Select(p => p.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Read_only_role_set_rejects_any_write_permission_and_unknown_codes()
    {
        Assert.True(PermissionCatalog.CreateSet(RoleKind.ReadOnly, [Permissions.Loans.View]).IsSuccess);
        Assert.Equal(
            "Role.ReadOnlyRoleCannotHoldWritePermission",
            PermissionCatalog.CreateSet(RoleKind.ReadOnly, [Permissions.Applications.Create]).Error?.Code);
        Assert.Equal(
            "Role.ReadOnlyRoleCannotHoldWritePermission",
            PermissionCatalog.CreateSet(RoleKind.ReadOnly, ["applications.unknown"]).Error?.Code);
    }
}
