using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Application.Roles;
using CreditFlow.Domain.Auditing;

namespace CreditFlow.Application.UnitTests.Roles;

public sealed class RoleAdministrationServiceTests
{
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeReferenceRepositories _roles = new();
    private readonly FakeAuditWriter _audit = new();
    private readonly FakeAccessCacheInvalidator _cache;
    private readonly RoleAdministrationService _service;

    public RoleAdministrationServiceTests()
    {
        _cache = new FakeAccessCacheInvalidator(_unitOfWork);
        _service = new RoleAdministrationService(_roles, _cache, _audit, _unitOfWork);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Changing_permissions_saves_audits_and_invalidates_the_role_after_commit()
    {
        var underwriter = TestReferenceData.RoleId(RoleCodes.Underwriter);
        var permissions = RoleSeedCatalog.Get(RoleCodes.Underwriter).Permissions
            .Where(p => p != Permissions.Reports.View).ToList();

        var result = await _service.UpdatePermissionsAsync(new UpdateRolePermissionsCommand(underwriter, permissions), Ct);

        Assert.True(result.IsSuccess);
        var details = await _roles.GetDetailsAsync(underwriter, Ct);
        Assert.DoesNotContain(Permissions.Reports.View, details!.Permissions);
        Assert.Equal([underwriter], _cache.InvalidatedRoles);
        Assert.False(_cache.InvalidatedInsideTransaction);
        var audited = Assert.Single(_audit.Events);
        Assert.Equal(AuditActions.RolePermissionsChanged, audited.Action);
        Assert.Contains(Permissions.Reports.View, audited.BeforeJson, StringComparison.Ordinal);
        Assert.DoesNotContain(Permissions.Reports.View, audited.AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Business_role_cannot_be_given_an_admin_permission()
    {
        var underwriter = TestReferenceData.RoleId(RoleCodes.Underwriter);

        var result = await _service.UpdatePermissionsAsync(
            new UpdateRolePermissionsCommand(underwriter, [Permissions.Applications.Decide, Permissions.Admin.UsersManage]),
            Ct);

        Assert.Equal("Role.BusinessRoleCannotHoldAdministrativePermission", result.Error?.Code);
        Assert.Empty(_cache.InvalidatedRoles);
    }

    [Fact]
    public async Task System_administrator_cannot_be_given_a_business_permission()
    {
        var admin = TestReferenceData.RoleId(RoleCodes.SystemAdministrator);
        var permissions = RoleSeedCatalog.Get(RoleCodes.SystemAdministrator).Permissions
            .Append(Permissions.Applications.ViewFull).ToList();

        var result = await _service.UpdatePermissionsAsync(new UpdateRolePermissionsCommand(admin, permissions), Ct);

        Assert.Equal("Role.AdministrativeRoleCannotHoldBusinessPermission", result.Error?.Code);
    }

    [Fact]
    public async Task System_administrator_must_keep_user_and_role_management()
    {
        var admin = TestReferenceData.RoleId(RoleCodes.SystemAdministrator);
        var permissions = RoleSeedCatalog.Get(RoleCodes.SystemAdministrator).Permissions
            .Where(p => p != Permissions.Admin.RolesManage).ToList();

        var result = await _service.UpdatePermissionsAsync(new UpdateRolePermissionsCommand(admin, permissions), Ct);

        Assert.Equal("Role.MustKeepAdministrationPermission", result.Error?.Code);
    }

    [Fact]
    public async Task Unknown_permissions_are_rejected()
    {
        var result = await _service.UpdatePermissionsAsync(
            new UpdateRolePermissionsCommand(TestReferenceData.RoleId(RoleCodes.Underwriter), ["applications.fly"]),
            Ct);

        Assert.Equal("Role.UnknownPermission", result.Error?.Code);
    }

    [Fact]
    public async Task Saving_unchanged_permissions_does_nothing()
    {
        var underwriter = TestReferenceData.RoleId(RoleCodes.Underwriter);

        var result = await _service.UpdatePermissionsAsync(
            new UpdateRolePermissionsCommand(underwriter, RoleSeedCatalog.Get(RoleCodes.Underwriter).Permissions),
            Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(_cache.InvalidatedRoles);
        Assert.Empty(_audit.Events);
    }

    [Fact]
    public async Task Editor_groups_by_module_and_marks_what_segregation_of_duties_forbids()
    {
        var editor = (await _service.GetPermissionEditorAsync(TestReferenceData.RoleId(RoleCodes.Underwriter), Ct)).Value;

        Assert.Equal(
            PermissionCatalog.Modules.Select(m => m.Key),
            editor.Groups.Select(g => g.ModuleKey));

        var items = editor.Groups.SelectMany(g => g.Items).ToList();
        Assert.All(items.Where(i => i.Code.StartsWith("admin.", StringComparison.Ordinal)), i => Assert.False(i.IsAssignable));
        Assert.True(items.Single(i => i.Code == Permissions.Applications.Decide).IsGranted);
        Assert.DoesNotContain(items, i => i.IsLocked);
    }

    [Fact]
    public async Task Editor_locks_the_permissions_the_administrator_role_must_keep()
    {
        var editor = (await _service.GetPermissionEditorAsync(
            TestReferenceData.RoleId(RoleCodes.SystemAdministrator), Ct)).Value;

        var locked = editor.Groups.SelectMany(g => g.Items).Where(i => i.IsLocked).Select(i => i.Code);
        Assert.Equal(RoleRules.RequiredAdministrativePermissions.Order(), locked.Order());
    }

    [Fact]
    public async Task Requiring_mfa_for_a_role_invalidates_the_role()
    {
        var hd = TestReferenceData.RoleId(RoleCodes.Head);

        var result = await _service.SetRequireMfaAsync(hd, true, Ct);

        Assert.True(result.IsSuccess);
        Assert.True((await _roles.GetDetailsAsync(hd, Ct))!.RequireMfa);
        Assert.Equal([hd], _cache.InvalidatedRoles);
    }
}
