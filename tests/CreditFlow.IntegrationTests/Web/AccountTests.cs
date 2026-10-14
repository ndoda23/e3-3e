using System.Net;
using System.Text.RegularExpressions;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Application.Roles;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Roles;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Sign-in, lockout, forced password change, temporary password expiry, logout and MFA against the real app.</summary>
public sealed partial class AccountTests(SqlServerContainer sql) : WebTestBase(sql)
{
    [GeneratedRegex("data-shared-key>(?<key>[^<]+)<")]
    private static partial Regex SharedKeyPattern();

    // ----- Sign-in -----

    [Fact]
    public async Task Operator_signs_in_with_user_name_and_lands_on_the_operator_dashboard()
    {
        var (userName, _, password) = await CreateOperatorAsync();
        using var browser = NewSession();

        var login = await browser.LoginAsync(userName, password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location!.OriginalString);

        var dashboard = await browser.GetPageAsync("/");
        Assert.Contains("data-dashboard=\"Operator\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Branch workspace", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Users_can_sign_in_with_their_email()
    {
        var (userName, _, password) = await CreateOperatorAsync();
        using var browser = NewSession();

        var login = await browser.LoginAsync($"{userName}@pcb.ge", password);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    [Fact]
    public async Task Sign_in_records_success_and_last_login()
    {
        var (userName, id, password) = await CreateOperatorAsync();
        using var browser = NewSession();

        await browser.LoginAsync(userName, password);

        var (audited, lastLogin) = await QueryAsync(async db => (
            await db.AuditLogs.AnyAsync(l => l.Action == AuditActions.LoginSucceeded && l.UserId == id.Value, Ct),
            await db.Users.Where(u => u.Id == id.Value).Select(u => u.LastLoginAt).SingleAsync(Ct)));
        Assert.True(audited);
        Assert.NotNull(lastLogin);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_get_the_same_generic_message_and_are_audited()
    {
        var (userName, id, _) = await CreateOperatorAsync();
        using var browser = NewSession();

        var wrongPassword = await (await browser.LoginAsync(userName, "Wrong!Passw0rd-xx")).Content.ReadAsStringAsync(Ct);
        var unknownUser = await (await browser.LoginAsync("nobody.here", "Wrong!Passw0rd-xx")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Invalid user name or password.", wrongPassword, StringComparison.Ordinal);
        Assert.Contains("Invalid user name or password.", unknownUser, StringComparison.Ordinal);

        var reasons = await QueryAsync(db => db.AuditLogs
            .Where(l => l.Action == AuditActions.LoginFailed)
            .Select(l => new { l.UserId, l.UserName, l.AfterJson })
            .ToListAsync(Ct));
        Assert.Contains(reasons, r => r.UserId == id.Value && r.AfterJson!.Contains("InvalidPassword", StringComparison.Ordinal));
        Assert.Contains(reasons, r => r.UserId == null && r.UserName == "nobody.here" && r.AfterJson!.Contains("UnknownUser", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inactive_users_cannot_sign_in_and_get_the_generic_message()
    {
        var (userName, id, password) = await CreateOperatorAsync();
        await DeactivateAsync(id);
        using var browser = NewSession();

        var response = await browser.LoginAsync(userName, password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid user name or password.", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    // ----- Lockout (Identity default: the lockout page is shown whenever the account is locked) -----

    [Fact]
    public async Task Five_failures_lock_the_account_and_even_the_correct_password_shows_the_lockout_page()
    {
        var (userName, id, password) = await CreateOperatorAsync();
        using var browser = NewSession();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await browser.LoginAsync(userName, "Wrong!Passw0rd-xx")).StatusCode);
        }

        var fifth = await browser.LoginAsync(userName, "Wrong!Passw0rd-xx");
        Assert.Equal("/Account/Lockout", fifth.Headers.Location!.OriginalString);

        var correct = await browser.LoginAsync(userName, password);
        Assert.Equal("/Account/Lockout", correct.Headers.Location!.OriginalString);

        var wrong = await browser.LoginAsync(userName, "Another!Wr0ng-pass");
        Assert.Equal("/Account/Lockout", wrong.Headers.Location!.OriginalString);

        Assert.True(await QueryAsync(db => db.AuditLogs.AnyAsync(l => l.Action == AuditActions.LockedOut && l.UserId == id.Value, Ct)));
        Assert.Contains("Account locked", await browser.GetPageAsync("/Account/Lockout"), StringComparison.Ordinal);
    }

    // ----- Temporary password -----

    [Fact]
    public async Task Seeded_administrator_must_change_the_password_before_anything_else()
    {
        using var browser = NewSession();

        var login = await browser.LoginAsync(InfrastructureHost.AdminUserName, InfrastructureHost.AdminPassword);
        Assert.Equal("/", login.Headers.Location!.OriginalString);

        var forced = await browser.GetAsync("/Admin/Users");
        Assert.Equal("/Account/ChangePassword", forced.Headers.Location!.OriginalString);

        var changed = await browser.PostFormAsync("/Account/ChangePassword", "/Account/ChangePassword", new Dictionary<string, string>
        {
            ["CurrentPassword"] = InfrastructureHost.AdminPassword,
            ["NewPassword"] = "Admin!Changed-Pass1",
            ["ConfirmPassword"] = "Admin!Changed-Pass1",
        });
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);

        // Administrators land on their dashboard (locked users, users without MFA, access changes).
        Assert.Contains("data-dashboard=\"Administration\"", await browser.GetPageAsync("/"), StringComparison.Ordinal);
        var users = await browser.GetPageAsync("/Admin");
        Assert.Contains("data-nav=\"Users\"", users, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", users, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_the_password_rejects_reuse_and_wrong_current_password()
    {
        var (userName, _, password) = await CreateUserAsync(RoleCodes.Auditor, temporary: true);
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var reuse = await browser.PostFormAsync("/Account/ChangePassword", "/Account/ChangePassword", new Dictionary<string, string>
        {
            ["CurrentPassword"] = password,
            ["NewPassword"] = password,
            ["ConfirmPassword"] = password,
        });
        Assert.Contains("You cannot reuse any of your last 5 passwords.", await reuse.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        var wrongCurrent = await browser.PostFormAsync("/Account/ChangePassword", "/Account/ChangePassword", new Dictionary<string, string>
        {
            ["CurrentPassword"] = "Not!The-Current1",
            ["NewPassword"] = "Brand!New-Passw0rd",
            ["ConfirmPassword"] = "Brand!New-Passw0rd",
        });
        Assert.Contains("The current password is not correct.", await wrongCurrent.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_temporary_password_cannot_be_used_until_an_administrator_issues_a_new_one()
    {
        var (userName, id, temporaryPassword) = await CreateUserAsync(RoleCodes.Auditor, temporary: true);
        Factory.Clock.Advance(TimeSpan.FromHours(72) + TimeSpan.FromMinutes(1));
        using var browser = NewSession();

        var expired = await browser.LoginAsync(userName, temporaryPassword);
        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        Assert.Contains(
            "Your temporary password has expired. Contact your administrator.",
            await expired.Content.ReadAsStringAsync(Ct),
            StringComparison.Ordinal);
        Assert.True(await QueryAsync(db => db.AuditLogs.AnyAsync(
            l => l.Action == AuditActions.LoginFailedTemporaryPasswordExpired && l.UserId == id.Value, Ct)));

        var reissued = await ResetPasswordAsync(id);
        var login = await browser.LoginAsync(userName, reissued);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Account/ChangePassword", (await browser.GetAsync("/")).Headers.Location!.OriginalString);
    }

    // ----- Return URL -----

    [Fact]
    public async Task Local_return_urls_are_followed_and_external_ones_ignored()
    {
        var (userName, _, password) = await CreateOperatorAsync();

        using (var browser = NewSession())
        {
            var local = await browser.LoginAsync(userName, password, "/Reports");
            Assert.Equal("/Reports", local.Headers.Location!.OriginalString);
        }

        using (var browser = NewSession())
        {
            var external = await browser.LoginAsync(userName, password, "https://evil.example/steal");
            Assert.Equal("/", external.Headers.Location!.OriginalString);
        }

        using (var browser = NewSession())
        {
            var protocolRelative = await browser.LoginAsync(userName, password, "//evil.example/steal");
            Assert.Equal("/", protocolRelative.Headers.Location!.OriginalString);
        }
    }

    // ----- Logout -----

    [Fact]
    public async Task Logout_is_post_only_needs_a_token_and_ends_the_session()
    {
        var (userName, id, password) = await CreateOperatorAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);
        var dashboard = await browser.GetPageAsync("/");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await browser.GetAsync("/Account/Logout")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostWithoutTokenAsync("/Account/Logout", new Dictionary<string, string>())).StatusCode);

        var logout = await browser.SubmitFormAsync(dashboard, "/Account/Logout", new Dictionary<string, string>());
        Assert.Equal("/Account/Login", logout.Headers.Location!.OriginalString);
        Assert.Contains(
            logout.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("__Host-CreditFlow.Auth=;", StringComparison.Ordinal) && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

        var after = await browser.GetAsync("/");
        Assert.StartsWith("https://localhost/Account/Login", after.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.True(await QueryAsync(db => db.AuditLogs.AnyAsync(l => l.Action == AuditActions.Logout && l.UserId == id.Value, Ct)));
    }

    [Fact]
    public async Task Auth_cookie_is_host_prefixed_http_only_secure_strict_and_not_persistent()
    {
        var (userName, _, password) = await CreateOperatorAsync();
        using var browser = NewSession();

        var login = await browser.LoginAsync(userName, password);

        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-CreditFlow.Auth=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    // ----- Access control after sign-in -----

    [Fact]
    public async Task Underwriter_is_denied_the_admin_area_and_sees_no_admin_menu_or_create_button()
    {
        var (userName, _, password) = await CreateUserAsync(RoleCodes.Underwriter);
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var dashboard = await browser.GetPageAsync("/");
        Assert.DoesNotContain("data-nav=\"Users\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("Create Application", dashboard, StringComparison.Ordinal);
        Assert.Contains("data-nav=\"Reports\"", dashboard, StringComparison.Ordinal);

        var admin = await browser.GetAsync("/Admin/Users");
        Assert.StartsWith("https://localhost/Account/AccessDenied", admin.Headers.Location!.ToString(), StringComparison.Ordinal);
        var denied = await browser.GetAsync("/Account/AccessDenied");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    // ----- MFA -----

    [Fact]
    public async Task Role_requiring_mfa_forces_enrolment_and_then_a_second_step_at_every_sign_in()
    {
        await RequireMfaForRoleAsync(RoleCodes.Auditor);
        var (userName, id, password) = await CreateUserAsync(RoleCodes.Auditor);
        using var browser = NewSession();

        // First sign-in: no MFA yet → everything leads to the set-up page.
        await browser.LoginAsync(userName, password);
        Assert.Equal("/Account/EnableAuthenticator", (await browser.GetAsync("/")).Headers.Location!.OriginalString);

        var setup = await browser.GetPageAsync("/Account/EnableAuthenticator");
        Assert.Contains("<svg class=\"qr\"", setup, StringComparison.Ordinal);
        var key = SharedKeyPattern().Match(setup).Groups["key"].Value;

        var enabled = await browser.SubmitFormAsync(
            setup, "/Account/EnableAuthenticator", new Dictionary<string, string> { ["Code"] = Totp.Compute(key) });
        var codesPage = await enabled.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Your recovery codes", codesPage, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);
        Assert.True(await QueryAsync(db => db.AuditLogs.AnyAsync(l => l.Action == AuditActions.MfaEnabled && l.UserId == id.Value, Ct)));

        // Next sign-in asks for the code.
        using var second = NewSession();
        var login = await second.LoginAsync(userName, password);
        Assert.StartsWith("/Account/LoginWith2fa", login.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var wrong = await second.PostFormAsync("/Account/LoginWith2fa", "/Account/LoginWith2fa", new Dictionary<string, string> { ["Code"] = "000000" });
        Assert.Contains("The code is not valid.", await wrong.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        var right = await second.PostFormAsync("/Account/LoginWith2fa", "/Account/LoginWith2fa", new Dictionary<string, string> { ["Code"] = Totp.Compute(key) });
        Assert.Equal("/", right.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task A_user_whose_mfa_is_required_cannot_turn_it_off()
    {
        await RequireMfaForRoleAsync(RoleCodes.Auditor);
        var (userName, _, password) = await CreateUserAsync(RoleCodes.Auditor);
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);
        var setup = await browser.GetPageAsync("/Account/EnableAuthenticator");
        var key = SharedKeyPattern().Match(setup).Groups["key"].Value;
        await browser.SubmitFormAsync(setup, "/Account/EnableAuthenticator", new Dictionary<string, string> { ["Code"] = Totp.Compute(key) });

        var page = await browser.GetPageAsync("/Account/Manage/TwoFactor");
        Assert.DoesNotContain("Turn off", page, StringComparison.Ordinal);

        var forced = await browser.PostWithSessionTokenAsync("/Account/Manage/TwoFactor/Disable", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, forced.StatusCode);
        Assert.Contains("cannot be turned off", await browser.GetPageAsync("/Account/Manage/TwoFactor"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task My_account_shows_the_profile()
    {
        var (userName, _, password) = await CreateOperatorAsync();
        using var browser = NewSession();
        await browser.LoginAsync(userName, password);

        var page = await browser.GetPageAsync("/Account/Manage");

        Assert.Contains(userName, page, StringComparison.Ordinal);
        Assert.Contains("Vake", page, StringComparison.Ordinal);
        Assert.Contains("Operator", page, StringComparison.Ordinal);
    }

    private async Task RequireMfaForRoleAsync(string roleCode)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var roleId = await services.GetRequiredService<CreditFlow.Infrastructure.Persistence.CreditFlowDbContext>()
            .Roles.Where(r => r.Code == roleCode).Select(r => r.Id).SingleAsync(Ct);
        var result = await services.GetRequiredService<RoleAdministrationService>().SetRequireMfaAsync(new RoleId(roleId), true, Ct);
        Assert.True(result.IsSuccess);
    }

    private async Task DeactivateAsync(CreditFlow.Domain.Users.UserId id)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CreditFlow.Infrastructure.Persistence.CreditFlowDbContext>();
        var staff = await db.StaffMembers.SingleAsync(s => s.Id == id, Ct);
        staff.Deactivate();
        await db.SaveChangesAsync(Ct);
    }

    private async Task<string> ResetPasswordAsync(CreditFlow.Domain.Users.UserId id)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<UserAdministrationService>().ResetPasswordAsync(id, Ct);
        Assert.True(result.IsSuccess);
        return result.Value.TemporaryPassword;
    }
}

/// <summary>The sign-in rate limit (per client IP) returns 429 with the friendly page.</summary>
public sealed class SignInRateLimitTests(SqlServerContainer sql) : WebTestBase(sql)
{
    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?> { ["Security:SignInAttemptsPerMinute"] = "3" };

    [Fact]
    public async Task Too_many_sign_in_attempts_are_rejected_with_429()
    {
        using var browser = NewSession();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await browser.LoginAsync("someone", "Wrong!Passw0rd-xx")).StatusCode);
        }

        var limited = await browser.LoginAsync("someone", "Wrong!Passw0rd-xx");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("Too many attempts", await limited.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
