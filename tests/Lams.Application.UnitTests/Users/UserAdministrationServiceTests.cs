using Lams.Application.Common;
using Lams.Application.ReferenceData;
using Lams.Application.Users;
using Lams.Domain.Auditing;
using Lams.Domain.Branches;
using Lams.Domain.Common;
using Lams.Domain.Users;

namespace Lams.Application.UnitTests.Users;

public sealed class UserAdministrationServiceTests
{
    private readonly UserAdministrationFixture _fixture = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ----- Create -----

    [Fact]
    public async Task Creating_a_senior_underwriter_in_Vake_issues_a_temporary_password()
    {
        var result = await _fixture.Service.CreateAsync(NewUnderwriter(), Ct);

        Assert.True(result.IsSuccess, result.Error?.Description);
        var issued = result.Value;

        var member = _fixture.Staff.Members[issued.UserId];
        Assert.Equal(TestReferenceData.RoleId(RoleCodes.Underwriter), member.RoleId);
        Assert.Equal(_fixture.Vake, member.BranchId);
        Assert.Equal(AuthorityLevel.Senior, member.AuthorityLevel);

        var account = Assert.Single(_fixture.Identity.CreatedAccounts);
        Assert.Equal(issued.UserId, account.Id);
        Assert.Equal(issued.TemporaryPassword, account.TemporaryPassword);
        Assert.Equal(_fixture.Clock.GetUtcNow().AddHours(72), issued.ExpiresAt);
        Assert.Equal(issued.ExpiresAt, account.TemporaryPasswordExpiresAt);
    }

    [Fact]
    public async Task Account_and_staff_member_are_created_in_one_transaction()
    {
        await _fixture.Service.CreateAsync(NewUnderwriter(), Ct);

        Assert.Equal(1, _fixture.UnitOfWork.Commits);
        Assert.False(_fixture.Identity.WroteOutsideTransaction);
    }

    [Fact]
    public async Task Create_reports_every_missing_attribute_of_the_role_at_once()
    {
        var result = await _fixture.Service.CreateAsync(
            NewUnderwriter() with { BranchId = null, AuthorityLevel = null }, Ct);

        Assert.Equal(Error.ValidationFailedCode, result.Error?.Code);
        Assert.Equal(
            [UserErrors.BranchRequired.Code, UserErrors.AuthorityLevelRequired.Code],
            result.Error!.Details.Select(d => d.Code));
        Assert.Empty(_fixture.Identity.CreatedAccounts);
    }

    [Fact]
    public async Task Create_reports_field_errors_and_role_attribute_errors_together()
    {
        var result = await _fixture.Service.CreateAsync(
            NewUnderwriter() with { UserName = "x", BranchId = null, AuthorityLevel = null }, Ct);

        Assert.Equal(
            [UserFieldRules.InvalidUserNameCode, UserErrors.BranchRequired.Code, UserErrors.AuthorityLevelRequired.Code],
            result.Error!.Details.Select(d => d.Code));
    }

    [Fact]
    public async Task Create_rejects_an_inactive_branch()
    {
        var result = await _fixture.Service.CreateAsync(NewUnderwriter() with { BranchId = _fixture.ClosedBranch }, Ct);

        Assert.Equal(BranchErrors.Inactive, result.Error);
    }

    [Fact]
    public async Task Create_rejects_a_taken_user_name_or_email()
    {
        var result = await _fixture.Service.CreateAsync(
            NewUnderwriter() with { UserName = "ADMIN1", Email = "admin1@pcb.ge" }, Ct);

        Assert.Equal(
            [UserAdministrationErrors.UserNameTaken.Code, UserAdministrationErrors.EmailTaken.Code],
            result.Error!.Details.Select(d => d.Code));
    }

    [Fact]
    public async Task Creating_a_system_administrator_clears_business_attributes()
    {
        var result = await _fixture.Service.CreateAsync(
            NewUnderwriter() with { RoleId = TestReferenceData.RoleId(RoleCodes.SystemAdministrator), UserName = "admin2", Email = "admin2@pcb.ge" },
            Ct);

        var member = _fixture.Staff.Members[result.Value.UserId];
        Assert.Null(member.BranchId);
        Assert.Null(member.AuthorityLevel);
    }

    // ----- Guard rails -----

    [Fact]
    public async Task Administrator_cannot_deactivate_themselves()
    {
        _fixture.AddMember(TestReferenceData.Staff(RoleCodes.SystemAdministrator), "admin2");

        var result = await _fixture.Service.DeactivateAsync(_fixture.Admin.Id, Ct);

        Assert.Equal(UserErrors.CannotDeactivateSelf, result.Error);
        Assert.True(_fixture.Admin.IsActive);
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_be_deactivated_by_another_user()
    {
        _fixture.CurrentUser.UserId = UserId.New();

        var result = await _fixture.Service.DeactivateAsync(_fixture.Admin.Id, Ct);

        Assert.Equal(UserErrors.LastActiveAdministrator, result.Error);
    }

    [Fact]
    public async Task Administrator_cannot_demote_themselves()
    {
        _fixture.AddMember(TestReferenceData.Staff(RoleCodes.SystemAdministrator), "admin2");
        var command = _fixture.UnchangedUpdate(_fixture.Admin) with
        {
            RoleId = TestReferenceData.RoleId(RoleCodes.Director),
        };

        var result = await _fixture.Service.UpdateAsync(command, Ct);

        Assert.Equal(UserErrors.CannotDemoteSelf, result.Error);
        Assert.Equal(TestReferenceData.RoleId(RoleCodes.SystemAdministrator), _fixture.Admin.RoleId);
    }

    [Fact]
    public async Task Another_administrator_can_be_demoted_while_one_remains()
    {
        var second = _fixture.AddMember(TestReferenceData.Staff(RoleCodes.SystemAdministrator), "admin2");
        var command = _fixture.UnchangedUpdate(second) with { RoleId = TestReferenceData.RoleId(RoleCodes.Director) };

        var result = await _fixture.Service.UpdateAsync(command, Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Actions_need_a_signed_in_administrator()
    {
        var member = _fixture.AddSeniorUnderwriter();
        _fixture.CurrentUser.UserId = null;

        Assert.Equal(CommonErrors.NotAuthenticated, (await _fixture.Service.DeactivateAsync(member.Id, Ct)).Error);
        Assert.Equal(
            CommonErrors.NotAuthenticated,
            (await _fixture.Service.UpdateAsync(_fixture.UnchangedUpdate(member), Ct)).Error);
    }

    [Fact]
    public async Task Unknown_user_is_reported_as_not_found()
    {
        Assert.Equal(UserErrors.NotFound, (await _fixture.Service.DeactivateAsync(UserId.New(), Ct)).Error);
        Assert.Equal(UserErrors.NotFound, (await _fixture.Service.ResetPasswordAsync(UserId.New(), Ct)).Error);
    }

    // ----- Account actions -----

    [Fact]
    public async Task Reset_password_issues_a_new_temporary_password_and_is_audited()
    {
        var member = _fixture.AddSeniorUnderwriter();

        var result = await _fixture.Service.ResetPasswordAsync(member.Id, Ct);

        Assert.True(result.IsSuccess);
        var (id, password, expiresAt) = Assert.Single(_fixture.Identity.TemporaryPasswords);
        Assert.Equal(member.Id, id);
        Assert.Equal(result.Value.TemporaryPassword, password);
        Assert.Equal(_fixture.Clock.GetUtcNow().AddHours(72), expiresAt);
        Assert.Contains(_fixture.Audit.Events, e => e.Action == AuditActions.PasswordReset);
    }

    [Fact]
    public async Task Force_sign_out_rotates_the_security_stamp_and_is_audited()
    {
        var member = _fixture.AddSeniorUnderwriter();

        var result = await _fixture.Service.ForceSignOutAsync(member.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([member.Id], _fixture.Identity.SessionInvalidations);
        Assert.Contains(_fixture.Audit.Events, e => e.Action == AuditActions.ForcedSignOut);
    }

    [Fact]
    public async Task Unlock_and_require_mfa_are_audited()
    {
        var member = _fixture.AddSeniorUnderwriter();

        await _fixture.Service.UnlockAsync(member.Id, Ct);
        await _fixture.Service.SetRequireMfaAsync(member.Id, true, Ct);

        Assert.Equal([member.Id], _fixture.Identity.Unlocked);
        Assert.True(_fixture.Identity.Accounts[member.Id].RequireMfa);
        Assert.Equal(
            [AuditActions.UserUnlocked, AuditActions.MfaRequirementChanged],
            _fixture.Audit.Events.Select(e => e.Action));
    }

    [Fact]
    public async Task Reset_password_and_mfa_requirement_refresh_the_cached_account_state_after_commit()
    {
        var member = _fixture.AddSeniorUnderwriter();

        await _fixture.Service.ResetPasswordAsync(member.Id, Ct);
        await _fixture.Service.SetRequireMfaAsync(member.Id, true, Ct);

        Assert.Equal([member.Id, member.Id], _fixture.Cache.InvalidatedUsers);
        Assert.False(_fixture.Cache.InvalidatedInsideTransaction);
    }

    [Fact]
    public async Task Inactive_user_cannot_be_marked_available()
    {
        var member = _fixture.AddSeniorUnderwriter();
        member.Deactivate();

        var result = await _fixture.Service.SetAvailabilityAsync(member.Id, true, Ct);

        Assert.Equal(UserErrors.InactiveCannotBeAvailable, result.Error);
    }

    private CreateUserCommand NewUnderwriter() => new()
    {
        UserName = "tamta.tatishvili",
        Email = "tamta.t@pcb.ge",
        FullName = "Tamta Tatishvili",
        RoleId = TestReferenceData.RoleId(RoleCodes.Underwriter),
        BranchId = _fixture.Vake,
        AuthorityLevel = AuthorityLevel.Senior,
    };
}
