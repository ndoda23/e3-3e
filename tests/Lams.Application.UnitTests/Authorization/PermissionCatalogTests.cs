using System.Reflection;
using Lams.Application.Authorization;
using Lams.Domain.Roles;

namespace Lams.Application.UnitTests.Authorization;

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
    public void There_are_nine_administration_permissions()
    {
        Assert.Equal(9, PermissionCatalog.All.Count(p => p.IsAdministrative));
    }

    [Fact]
    public void Lookup_by_code()
    {
        Assert.True(PermissionCatalog.Contains(Permissions.Applications.Decide));
        Assert.False(PermissionCatalog.Contains("applications.unknown"));
        Assert.Equal(PermissionCatalog.ApplicationsModule, PermissionCatalog.Find(Permissions.Applications.Decide)!.Module);
    }
}
