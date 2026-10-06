using Lams.Domain.Roles;

namespace Lams.Domain.UnitTests.Roles;

/// <summary>Segregation of duties between administration and business permissions.</summary>
public sealed class PermissionSetTests
{
    [Fact]
    public void Business_role_may_hold_business_permissions()
    {
        var result = PermissionSet.Create(roleIsAdministrative: false, ["applications.decide", "reports.view"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.True(result.Value.Contains("applications.decide"));
    }

    [Fact]
    public void Business_role_may_never_hold_an_admin_permission()
    {
        var result = PermissionSet.Create(roleIsAdministrative: false, ["applications.decide", "admin.users.manage"]);

        Assert.Equal("Role.BusinessRoleCannotHoldAdministrativePermission", result.Error?.Code);
    }

    [Fact]
    public void Administrative_role_may_hold_only_admin_permissions()
    {
        var result = PermissionSet.Create(roleIsAdministrative: true, ["admin.users.manage", "admin.roles.manage"]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Administrative_role_may_not_hold_a_business_permission()
    {
        var result = PermissionSet.Create(roleIsAdministrative: true, ["admin.users.manage", "applications.view-full"]);

        Assert.Equal("Role.AdministrativeRoleCannotHoldBusinessPermission", result.Error?.Code);
    }

    [Fact]
    public void Duplicates_are_collapsed()
    {
        var result = PermissionSet.Create(false, ["reports.view", "reports.view", " reports.view "]);

        Assert.Equal(1, result.Value.Count);
    }

    [Fact]
    public void Malformed_codes_are_rejected()
    {
        var result = PermissionSet.Create(false, ["reports.view", "NotAPermission"]);

        Assert.Equal("Role.InvalidPermission", result.Error?.Code);
    }

    [Fact]
    public void An_empty_set_is_allowed()
    {
        Assert.True(PermissionSet.Create(false, []).IsSuccess);
    }

    [Fact]
    public void Contains_is_false_for_unknown_or_malformed_codes()
    {
        var set = PermissionSet.Create(false, ["reports.view"]).Value;

        Assert.False(set.Contains("applications.decide"));
        Assert.False(set.Contains("not valid"));
    }
}
