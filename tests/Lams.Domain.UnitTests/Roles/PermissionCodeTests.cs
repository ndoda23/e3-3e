using Lams.Domain.Roles;

namespace Lams.Domain.UnitTests.Roles;

public sealed class PermissionCodeTests
{
    [Theory]
    [InlineData("applications.decide")]
    [InlineData("applications.view-full")]
    [InlineData("admin.authority-limits.manage")]
    [InlineData("documents.delete-own-new")]
    public void Well_formed_codes_are_accepted(string code)
    {
        Assert.True(PermissionCode.Create(code).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("applications")]
    [InlineData("Applications.Decide")]
    [InlineData("applications..decide")]
    [InlineData("applications.-decide")]
    [InlineData("applications.decide-")]
    [InlineData("applications.de cide")]
    public void Malformed_codes_are_rejected(string? code)
    {
        Assert.True(PermissionCode.Create(code).IsFailure);
    }

    [Theory]
    [InlineData("admin.users.manage", true)]
    [InlineData("admin.settings.manage", true)]
    [InlineData("applications.decide", false)]
    [InlineData("administration.view", false)]
    public void Administrative_permissions_are_those_under_admin(string code, bool expected)
    {
        Assert.Equal(expected, PermissionCode.Create(code).Value.IsAdministrative);
    }
}
