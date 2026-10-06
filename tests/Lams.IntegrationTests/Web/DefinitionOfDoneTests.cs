using System.Net;
using System.Text.RegularExpressions;
using Lams.Application.ReferenceData;
using Lams.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lams.IntegrationTests.Web;

/// <summary>
/// The Phase 1 definition of done, end to end through the UI only: the seeded administrator signs in, changes the
/// password, creates a Senior Underwriter in Vake; that user signs in with the temporary password, chooses their own,
/// sees only their menu and their authority limit, and is denied the admin area.
/// </summary>
public sealed partial class DefinitionOfDoneTests(SqlServerContainer sql) : WebTestBase(sql)
{
    private const string AdminNewPassword = "Admin!Changed-Pass1";
    private const string UnderwriterPassword = "Tamta!Own-Passw0rd";

    [GeneratedRegex("data-temp-password>(?<password>[^<]+)<")]
    private static partial Regex TemporaryPasswordPattern();

    [GeneratedRegex("data-nav=\"(?<key>[^\"]+)\"")]
    private static partial Regex NavItemPattern();

    [Fact]
    public async Task Administrator_creates_a_senior_underwriter_in_Vake_who_gets_only_underwriter_access()
    {
        // 1. Seeded administrator: forced password change, then Admin › Users.
        using var admin = NewSession();
        await admin.LoginAsync(InfrastructureHost.AdminUserName, InfrastructureHost.AdminPassword);
        await ChangePasswordAsync(admin, InfrastructureHost.AdminPassword, AdminNewPassword);
        Assert.Equal("/Admin", (await admin.GetAsync("/")).Headers.Location!.OriginalString);

        // 2. Create the Senior Underwriter in Vake through the form; the temporary password is shown once.
        var (uwRole, vake) = await QueryAsync(async db => (
            await db.Roles.Where(r => r.Code == RoleCodes.Underwriter).Select(r => r.Id).SingleAsync(Ct),
            (await db.Branches.Where(b => b.Code == BranchSeedCatalog.Vake).Select(b => b.Id).SingleAsync(Ct)).Value));
        var created = await admin.PostFormAsync("/Admin/Users/Create", "/Admin/Users/Create", new Dictionary<string, string>
        {
            ["FullName"] = "Tamta Tatishvili",
            ["UserName"] = "tamta.tatishvili",
            ["Email"] = "tamta.t@pcb.ge",
            ["RoleId"] = uwRole.ToString(),
            ["BranchId"] = vake.ToString(),
            ["AuthorityLevel"] = "Senior",
            ["Capacity"] = "12",
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var temporaryPassword = WebUtility.HtmlDecode(
            TemporaryPasswordPattern().Match(await created.Content.ReadAsStringAsync(Ct)).Groups["password"].Value);

        // 3. The new user signs in in another browser and must choose their own password first.
        using var underwriter = NewSession();
        await underwriter.LoginAsync("tamta.tatishvili", temporaryPassword);
        Assert.Equal("/Account/ChangePassword", (await underwriter.GetAsync("/")).Headers.Location!.OriginalString);
        await ChangePasswordAsync(underwriter, temporaryPassword, UnderwriterPassword);

        // 4. Underwriter dashboard: only the underwriter menu, no admin section, no Create Application.
        var dashboard = WebUtility.HtmlDecode(await underwriter.GetPageAsync("/"));
        Assert.Contains("data-dashboard=\"Underwriter\"", dashboard, StringComparison.Ordinal);
        Assert.Equal(
            ["Dashboard", "Applications", "Packages", "Reports"],
            NavItemPattern().Matches(dashboard).Select(m => m.Groups["key"].Value));
        Assert.DoesNotContain("href=\"/Applications/New\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("data-dashboard-chip>Sr UW · Tamta Tatishvili<", dashboard, StringComparison.Ordinal);
        Assert.Contains("₾50,000", dashboard, StringComparison.Ordinal);
        Assert.Contains("data-user-role>Senior Underwriter<", dashboard, StringComparison.Ordinal);

        // 5. Admin area denied: page request → Access denied page; htmx request → 403 with HX-Redirect.
        var page = await underwriter.GetAsync("/Admin/Users");
        Assert.Equal("/Account/AccessDenied", page.Headers.Location!.AbsolutePath);
        var htmx = await underwriter.HtmxGetAsync("/Admin/Users/Rows");
        Assert.Equal(HttpStatusCode.Forbidden, htmx.StatusCode);
        Assert.Equal("/Account/AccessDenied", htmx.Headers.GetValues("HX-Redirect").Single());
    }

    private static async Task ChangePasswordAsync(WebSession browser, string current, string newPassword)
    {
        var response = await browser.PostFormAsync("/Account/ChangePassword", "/Account/ChangePassword", new Dictionary<string, string>
        {
            ["CurrentPassword"] = current,
            ["NewPassword"] = newPassword,
            ["ConfirmPassword"] = newPassword,
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
