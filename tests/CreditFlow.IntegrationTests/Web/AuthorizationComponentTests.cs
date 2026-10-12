using System.Security.Claims;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Users;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Policy provider, permission handler and tag helper, with a fake profile source (no database).</summary>
public sealed class AuthorizationComponentTests
{
    private sealed class FakeProfiles(UserAccessProfile? profile) : IUserAccessProfileService
    {
        public Task<UserAccessProfile?> GetAsync(UserId userId, CancellationToken cancellationToken) =>
            Task.FromResult(profile is null ? null : profile with { UserId = userId });

        public Task<AccountSecurityState?> GetSecurityStateAsync(UserId userId, CancellationToken cancellationToken) =>
            Task.FromResult<AccountSecurityState?>(profile is null ? null : new AccountSecurityState(false, false, false));
    }

    [Fact]
    public async Task Permission_policies_are_built_on_demand()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        var single = await provider.GetPolicyAsync(Policies.PermissionPrefix + Permissions.Admin.UsersManage);
        var any = await provider.GetPolicyAsync(
            Policies.AnyPermissionPrefix + Permissions.Compliance.Review + "," + Permissions.Committee.Vote);

        Assert.Equal([Permissions.Admin.UsersManage], single!.Requirements.OfType<PermissionRequirement>().Single().Permissions);
        Assert.Equal(2, any!.Requirements.OfType<PermissionRequirement>().Single().Permissions.Count);
        Assert.Contains(single.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task Unknown_permissions_in_policy_names_fail_fast()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetPolicyAsync("perm:applications.fly"));
    }

    [Fact]
    public async Task Other_policy_names_fall_back_to_registered_policies()
    {
        var options = new AuthorizationOptions();
        options.AddPolicy("custom", p => p.RequireAuthenticatedUser());
        var provider = new PermissionPolicyProvider(Options.Create(options));

        Assert.NotNull(await provider.GetPolicyAsync("custom"));
        Assert.Null(await provider.GetPolicyAsync("missing"));
    }

    [Theory]
    [InlineData(RoleCodes.Admin, true)]
    [InlineData(RoleCodes.Underwriter, false)]
    public async Task Permission_handler_succeeds_only_with_the_permission(string roleCode, bool expected)
    {
        var (access, user) = Signed(MenuTests.ProfileFor(roleCode));
        var requirement = new PermissionRequirement([Permissions.Admin.UsersManage]);
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await new PermissionHandler(access).HandleAsync(context);

        Assert.Equal(expected, context.HasSucceeded);
    }

    [Fact]
    public async Task Handlers_deny_users_without_a_profile()
    {
        var (access, user) = Signed(null);
        var permission = new AuthorizationHandlerContext([new PermissionRequirement([Permissions.Reports.View])], user, null);
        var admin = new AuthorizationHandlerContext([AdministrationRequirement.Instance], user, null);

        await new PermissionHandler(access).HandleAsync(permission);
        await new AdministrationHandler(access).HandleAsync(admin);

        Assert.False(permission.HasSucceeded);
        Assert.False(admin.HasSucceeded);
    }

    [Theory]
    [InlineData(RoleCodes.Operator, "applications.create", null, true)]
    [InlineData(RoleCodes.Underwriter, "applications.create", null, false)]
    [InlineData(RoleCodes.Underwriter, null, "admin.users.view,reports.view", true)]
    [InlineData(RoleCodes.Underwriter, null, "admin.users.view,admin.roles.manage", false)]
    public async Task Tag_helper_renders_only_with_the_permission(
        string roleCode, string? permission, string? anyPermission, bool rendered)
    {
        var (access, _) = Signed(MenuTests.ProfileFor(roleCode));
        var helper = new PermissionTagHelper(access) { Permission = permission, AnyPermission = anyPermission };
        var output = new TagHelperOutput("a", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        await helper.ProcessAsync(new TagHelperContext([], new Dictionary<object, object>(), "id"), output);

        Assert.Equal(rendered, output.TagName is not null);
    }

    [Theory]
    [InlineData("https://localhost/Admin/Users?page=2", "/Admin/Users?page=2")]
    [InlineData("https://evil.example/x", null)]
    [InlineData("not a url", null)]
    public void Htmx_current_url_must_be_local(string header, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("localhost");
        context.Request.Headers[HtmxExtensions.CurrentUrlHeader] = header;

        Assert.Equal(expected, context.Request.HtmxCurrentLocalUrl());
    }

    private static (CurrentAccess Access, ClaimsPrincipal User) Signed(UserAccessProfile? profile)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } };
        return (new CurrentAccess(accessor, new FakeProfiles(profile)), user);
    }
}
