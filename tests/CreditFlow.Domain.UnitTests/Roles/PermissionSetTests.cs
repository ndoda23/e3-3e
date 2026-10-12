using CreditFlow.Domain.Roles;

namespace CreditFlow.Domain.UnitTests.Roles;

/// <summary>Segregation of duties by role kind: business, read-only and administrative.</summary>
public sealed class PermissionSetTests
{
    /// <summary>Stands in for the permission catalog: codes ending in <c>.view</c> read only.</summary>
    private static readonly Func<PermissionCode, bool> IsReadOnly =
        code => code.Value.EndsWith(".view", StringComparison.Ordinal);

    [Fact]
    public void Business_role_may_hold_business_permissions()
    {
        var result = PermissionSet.Create(RoleKind.Business, ["underwriting.decide", "reports.view"], IsReadOnly);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.True(result.Value.Contains("underwriting.decide"));
    }

    [Fact]
    public void Business_role_may_never_hold_an_admin_permission()
    {
        var result = PermissionSet.Create(RoleKind.Business, ["underwriting.decide", "admin.users.manage"], IsReadOnly);

        Assert.Equal("Role.BusinessRoleCannotHoldAdministrativePermission", result.Error?.Code);
    }

    [Fact]
    public void Administrative_role_may_hold_only_admin_permissions()
    {
        var result = PermissionSet.Create(
            RoleKind.Administrative, ["admin.users.manage", "admin.roles.manage"], IsReadOnly);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Administrative_role_may_not_hold_a_business_permission()
    {
        var result = PermissionSet.Create(
            RoleKind.Administrative, ["admin.users.manage", "applications.view"], IsReadOnly);

        Assert.Equal("Role.AdministrativeRoleCannotHoldBusinessPermission", result.Error?.Code);
    }

    [Fact]
    public void Read_only_role_may_hold_read_permissions()
    {
        var result = PermissionSet.Create(RoleKind.ReadOnly, ["applications.view", "audit.view"], IsReadOnly);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Read_only_role_may_not_hold_a_write_permission()
    {
        var result = PermissionSet.Create(RoleKind.ReadOnly, ["applications.view", "underwriting.decide"], IsReadOnly);

        Assert.Equal("Role.ReadOnlyRoleCannotHoldWritePermission", result.Error?.Code);
    }

    [Fact]
    public void Read_only_role_may_not_hold_an_admin_permission_even_a_read_one()
    {
        var result = PermissionSet.Create(RoleKind.ReadOnly, ["admin.audit.view"], IsReadOnly);

        Assert.Equal("Role.BusinessRoleCannotHoldAdministrativePermission", result.Error?.Code);
    }

    [Fact]
    public void Duplicates_are_collapsed()
    {
        var result = PermissionSet.Create(RoleKind.Business, ["reports.view", "reports.view", " reports.view "], IsReadOnly);

        Assert.Equal(1, result.Value.Count);
    }

    [Fact]
    public void Malformed_codes_are_rejected()
    {
        var result = PermissionSet.Create(RoleKind.Business, ["reports.view", "NotAPermission"], IsReadOnly);

        Assert.Equal("Role.InvalidPermission", result.Error?.Code);
    }

    [Fact]
    public void An_empty_set_is_allowed()
    {
        Assert.True(PermissionSet.Create(RoleKind.ReadOnly, [], IsReadOnly).IsSuccess);
    }

    [Fact]
    public void Contains_is_false_for_unknown_or_malformed_codes()
    {
        var set = PermissionSet.Create(RoleKind.Business, ["reports.view"], IsReadOnly).Value;

        Assert.False(set.Contains("underwriting.decide"));
        Assert.False(set.Contains("not valid"));
    }
}
