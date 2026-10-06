using System.Collections;
using System.Reflection;
using Lams.Domain.Branches;
using Lams.Domain.Common;
using Lams.Domain.Roles;
using Lams.Domain.Users;

namespace Lams.Domain.UnitTests.Users;

public sealed class StaffMemberTests
{
    private static readonly BranchId Vake = BranchId.New();
    private static readonly BranchId Saburtalo = BranchId.New();

    // ----- Creation and attribute rules per role -----

    [Fact]
    public void Senior_underwriter_in_a_branch_is_created_active_and_available()
    {
        var result = StaffMember.Create(
            UserId.New(), " Tamta Tatishvili ", TestRoles.Underwriter, Vake, null, AuthorityLevel.Senior);

        Assert.True(result.IsSuccess);
        var staff = result.Value;
        Assert.Equal("Tamta Tatishvili", staff.FullName);
        Assert.Equal(TestRoles.Underwriter.Id, staff.RoleId);
        Assert.Equal(Vake, staff.BranchId);
        Assert.Equal(AuthorityLevel.Senior, staff.AuthorityLevel);
        Assert.True(staff.IsActive);
        Assert.True(staff.IsAvailable);
        Assert.Equal(StaffMember.DefaultCapacity, staff.Capacity);
    }

    [Theory]
    [MemberData(nameof(RolesRequiringBranchAndLevel))]
    public void Loan_officer_and_underwriter_require_branch_and_authority_level(RoleProfile role)
    {
        var errors = StaffMember.ValidateAssignment(role, branchId: null, segment: null, authorityLevel: null);

        Assert.Equal([UserErrors.BranchRequired, UserErrors.AuthorityLevelRequired], errors);
    }

    public static TheoryData<RoleProfile> RolesRequiringBranchAndLevel =>
        [TestRoles.LoanOfficer, TestRoles.Underwriter];

    [Fact]
    public void Create_fails_on_the_first_attribute_error()
    {
        var result = StaffMember.Create(UserId.New(), "Nika Beridze", TestRoles.Underwriter, null, null, null);

        Assert.Equal(UserErrors.BranchRequired, result.Error);
    }

    [Fact]
    public void Branch_manager_requires_a_branch_and_has_no_authority_level()
    {
        Assert.Equal(
            UserErrors.BranchRequired,
            StaffMember.Create(UserId.New(), "Mariam Lomidze", TestRoles.BranchManager, null, null, null).Error);

        var staff = Create(TestRoles.BranchManager, Vake, null, AuthorityLevel.Senior);
        Assert.Null(staff.AuthorityLevel);
    }

    [Fact]
    public void Head_requires_a_segment()
    {
        var result = StaffMember.Create(UserId.New(), "Irakli Tsereteli", TestRoles.Head, null, null, null);

        Assert.Equal(UserErrors.SegmentRequired, result.Error);
    }

    [Theory]
    [InlineData(Segment.Retail)]
    [InlineData(Segment.Business)]
    public void Head_with_a_segment_is_valid(Segment segment)
    {
        Assert.Equal(segment, Create(TestRoles.Head, null, segment, null).Segment);
    }

    [Fact]
    public void Segment_is_cleared_for_roles_that_do_not_allow_it()
    {
        Assert.Null(Create(TestRoles.Director, null, Segment.Retail, null).Segment);
    }

    [Fact]
    public void Optional_branch_is_kept_when_given_and_may_be_omitted()
    {
        Assert.Equal(Vake, Create(TestRoles.BackOffice, Vake, null, null).BranchId);
        Assert.Null(Create(TestRoles.BackOffice, null, null, null).BranchId);
    }

    [Fact]
    public void System_administrator_has_no_branch_segment_or_level()
    {
        var staff = Create(TestRoles.SystemAdministrator, Vake, Segment.Business, AuthorityLevel.Senior);

        Assert.Null(staff.BranchId);
        Assert.Null(staff.Segment);
        Assert.Null(staff.AuthorityLevel);
    }

    [Fact]
    public void Undefined_enum_values_are_rejected()
    {
        Assert.Contains(
            UserErrors.InvalidSegment, StaffMember.ValidateAssignment(TestRoles.Head, null, (Segment)99, null));
        Assert.Contains(
            UserErrors.InvalidAuthorityLevel,
            StaffMember.ValidateAssignment(TestRoles.Underwriter, Vake, null, (AuthorityLevel)99));
    }

    [Fact]
    public void Errors_name_the_field_they_belong_to()
    {
        var errors = StaffMember.ValidateAssignment(TestRoles.Underwriter, null, null, null);

        Assert.Equal(["BranchId", "AuthorityLevel"], errors.Select(e => e.Target));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Full_name_is_required(string? fullName)
    {
        var result = StaffMember.Create(UserId.New(), fullName, TestRoles.Director, null, null, null);

        Assert.Equal(UserErrors.InvalidFullName, result.Error);
    }

    [Fact]
    public void Full_name_has_a_maximum_length()
    {
        var staff = Create(TestRoles.Director, null, null, null);

        Assert.Equal(
            UserErrors.InvalidFullName, staff.Rename(new string('x', StaffMember.FullNameMaxLength + 1)).Error);
    }

    // ----- One role per user (segregation of duties) -----

    [Fact]
    public void A_staff_member_holds_a_single_role_value_and_no_role_collection()
    {
        var properties = typeof(StaffMember).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.Single(properties, p => p.PropertyType == typeof(RoleId));
        Assert.DoesNotContain(
            properties,
            p => p.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(p.PropertyType));
    }

    [Fact]
    public void Assigning_a_role_replaces_the_previous_role_and_reapplies_its_rules()
    {
        var staff = Create(TestRoles.Underwriter, Vake, null, AuthorityLevel.Standard);

        var result = staff.AssignRole(TestRoles.SystemAdministrator, Vake, null, AuthorityLevel.Standard);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestRoles.SystemAdministrator.Id, staff.RoleId);
        Assert.Null(staff.BranchId);
        Assert.Null(staff.AuthorityLevel);
    }

    [Fact]
    public void A_failed_role_assignment_leaves_the_staff_member_unchanged()
    {
        var staff = Create(TestRoles.Director, null, null, null);

        var result = staff.AssignRole(TestRoles.Underwriter, null, null, null);

        Assert.Equal(UserErrors.BranchRequired, result.Error);
        Assert.Equal(TestRoles.Director.Id, staff.RoleId);
    }

    // ----- Attribute changes within the current role -----

    [Fact]
    public void Branch_can_be_changed_within_the_role_rules()
    {
        var staff = Create(TestRoles.Underwriter, Vake, null, AuthorityLevel.Senior);

        Assert.True(staff.ChangeBranch(TestRoles.Underwriter, Saburtalo).IsSuccess);
        Assert.Equal(Saburtalo, staff.BranchId);

        Assert.Equal(UserErrors.BranchRequired, staff.ChangeBranch(TestRoles.Underwriter, null).Error);
        Assert.Equal(Saburtalo, staff.BranchId);
    }

    [Fact]
    public void Segment_and_authority_level_can_be_changed_within_the_role_rules()
    {
        var head = Create(TestRoles.Head, null, Segment.Retail, null);
        Assert.True(head.ChangeSegment(TestRoles.Head, Segment.Business).IsSuccess);
        Assert.Equal(Segment.Business, head.Segment);

        var underwriter = Create(TestRoles.Underwriter, Vake, null, AuthorityLevel.Junior);
        Assert.True(underwriter.ChangeAuthorityLevel(TestRoles.Underwriter, AuthorityLevel.Senior).IsSuccess);
        Assert.Equal(AuthorityLevel.Senior, underwriter.AuthorityLevel);
        Assert.Equal(
            UserErrors.AuthorityLevelRequired,
            underwriter.ChangeAuthorityLevel(TestRoles.Underwriter, null).Error);
    }

    [Fact]
    public void Attribute_changes_require_the_current_role_profile()
    {
        var staff = Create(TestRoles.Underwriter, Vake, null, AuthorityLevel.Senior);

        Assert.Throws<InvalidOperationException>(() => staff.ChangeBranch(TestRoles.BranchManager, Saburtalo));
    }

    // ----- Activation, availability, capacity -----

    [Fact]
    public void Deactivation_also_makes_the_staff_member_unavailable()
    {
        var staff = Create(TestRoles.Director, null, null, null);

        Assert.True(staff.Deactivate().IsSuccess);

        Assert.False(staff.IsActive);
        Assert.False(staff.IsAvailable);
    }

    [Fact]
    public void An_inactive_staff_member_cannot_be_marked_available()
    {
        var staff = Create(TestRoles.Director, null, null, null);
        staff.Deactivate();

        Assert.Equal(UserErrors.InactiveCannotBeAvailable, staff.SetAvailability(true).Error);
        Assert.True(staff.SetAvailability(false).IsSuccess);
    }

    [Fact]
    public void Reactivated_staff_member_can_be_marked_available_again()
    {
        var staff = Create(TestRoles.Director, null, null, null);
        staff.Deactivate();
        staff.Activate();

        Assert.True(staff.IsActive);
        Assert.True(staff.SetAvailability(true).IsSuccess);
        Assert.True(staff.IsAvailable);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(StaffMember.MaxCapacity + 1)]
    public void Capacity_must_be_within_range(int capacity)
    {
        var staff = Create(TestRoles.Director, null, null, null);

        Assert.Equal(UserErrors.InvalidCapacity, staff.ChangeCapacity(capacity).Error);
        Assert.Equal(StaffMember.DefaultCapacity, staff.Capacity);
    }

    // ----- Access snapshot and audit stamps -----

    [Fact]
    public void Access_snapshot_changes_only_with_access_relevant_attributes()
    {
        var staff = Create(TestRoles.Underwriter, Vake, null, AuthorityLevel.Senior);
        var before = staff.Access;

        staff.Rename("Tamta T.");
        staff.ChangeCapacity(12);
        staff.SetAvailability(false);
        Assert.Equal(before, staff.Access);

        staff.ChangeBranch(TestRoles.Underwriter, Saburtalo);
        Assert.NotEqual(before, staff.Access);
    }

    [Fact]
    public void Deactivation_changes_the_access_snapshot()
    {
        var staff = Create(TestRoles.Director, null, null, null);
        var before = staff.Access;

        staff.Deactivate();

        Assert.NotEqual(before, staff.Access);
    }

    [Fact]
    public void Audit_stamps_are_set_through_the_audited_entity_interface()
    {
        var staff = Create(TestRoles.Director, null, null, null);
        var admin = UserId.New();
        var at = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

        ((IAuditedEntity)staff).MarkCreated(admin, at);
        ((IAuditedEntity)staff).MarkUpdated(admin, at.AddHours(1));

        Assert.Equal(at, staff.CreatedAt);
        Assert.Equal(admin, staff.CreatedBy);
        Assert.Equal(at.AddHours(1), staff.UpdatedAt);
        Assert.Equal(admin, staff.UpdatedBy);
    }

    [Fact]
    public void State_can_only_be_changed_through_methods()
    {
        var publicSetters = typeof(StaffMember)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name);

        Assert.Empty(publicSetters);
    }

    private static StaffMember Create(
        RoleProfile role, BranchId? branchId, Segment? segment, AuthorityLevel? authorityLevel) =>
        StaffMember.Create(UserId.New(), "Test User", role, branchId, segment, authorityLevel).Value;
}
