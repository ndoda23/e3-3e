using System.Collections;
using System.Reflection;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.UnitTests.Users;

public sealed class StaffMemberTests
{
    private static readonly BranchId Vake = BranchId.New();
    private static readonly BranchId Saburtalo = BranchId.New();

    // ----- Creation and attribute rules per role -----

    [Fact]
    public void Operator_in_a_branch_is_created_active_and_available()
    {
        var result = StaffMember.Create(UserId.New(), " Anna Gelashvili ", TestRoles.Operator, Vake);

        Assert.True(result.IsSuccess);
        var staff = result.Value;
        Assert.Equal("Anna Gelashvili", staff.FullName);
        Assert.Equal(TestRoles.Operator.Id, staff.RoleId);
        Assert.Equal(Vake, staff.BranchId);
        Assert.True(staff.IsActive);
        Assert.True(staff.IsAvailable);
        Assert.Equal(StaffMember.DefaultCapacity, staff.Capacity);
    }

    [Fact]
    public void Operator_requires_a_branch()
    {
        Assert.Equal([UserErrors.BranchRequired], StaffMember.ValidateAssignment(TestRoles.Operator, branchId: null));
        Assert.Equal(
            UserErrors.BranchRequired,
            StaffMember.Create(UserId.New(), "Salome Janelidze", TestRoles.Operator, null).Error);
    }

    [Fact]
    public void Branch_is_cleared_for_roles_that_do_not_allow_it()
    {
        Assert.Null(Create(TestRoles.Underwriter, Vake).BranchId);
        Assert.Null(Create(TestRoles.Admin, Vake).BranchId);
    }

    [Fact]
    public void Optional_branch_is_kept_when_given_and_may_be_omitted()
    {
        Assert.Equal(Vake, Create(TestRoles.OptionalBranch, Vake).BranchId);
        Assert.Null(Create(TestRoles.OptionalBranch, null).BranchId);
    }

    [Fact]
    public void Branch_errors_name_the_field_they_belong_to()
    {
        var errors = StaffMember.ValidateAssignment(TestRoles.Operator, null);

        Assert.Equal(["BranchId"], errors.Select(e => e.Target));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Full_name_is_required(string? fullName)
    {
        var result = StaffMember.Create(UserId.New(), fullName, TestRoles.Underwriter, null);

        Assert.Equal(UserErrors.InvalidFullName, result.Error);
    }

    [Fact]
    public void Full_name_has_a_maximum_length()
    {
        var staff = Create(TestRoles.Underwriter, null);

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
        var staff = Create(TestRoles.Operator, Vake);

        var result = staff.AssignRole(TestRoles.Admin, Vake);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestRoles.Admin.Id, staff.RoleId);
        Assert.Null(staff.BranchId);
    }

    [Fact]
    public void A_failed_role_assignment_leaves_the_staff_member_unchanged()
    {
        var staff = Create(TestRoles.Underwriter, null);

        var result = staff.AssignRole(TestRoles.Operator, null);

        Assert.Equal(UserErrors.BranchRequired, result.Error);
        Assert.Equal(TestRoles.Underwriter.Id, staff.RoleId);
    }

    // ----- Attribute changes within the current role -----

    [Fact]
    public void Branch_can_be_changed_within_the_role_rules()
    {
        var staff = Create(TestRoles.Operator, Vake);

        Assert.True(staff.ChangeBranch(TestRoles.Operator, Saburtalo).IsSuccess);
        Assert.Equal(Saburtalo, staff.BranchId);

        Assert.Equal(UserErrors.BranchRequired, staff.ChangeBranch(TestRoles.Operator, null).Error);
        Assert.Equal(Saburtalo, staff.BranchId);
    }

    [Fact]
    public void Attribute_changes_require_the_current_role_profile()
    {
        var staff = Create(TestRoles.Operator, Vake);

        Assert.Throws<InvalidOperationException>(() => staff.ChangeBranch(TestRoles.OptionalBranch, Saburtalo));
    }

    // ----- Activation, availability, capacity -----

    [Fact]
    public void Deactivation_also_makes_the_staff_member_unavailable()
    {
        var staff = Create(TestRoles.Underwriter, null);

        Assert.True(staff.Deactivate().IsSuccess);

        Assert.False(staff.IsActive);
        Assert.False(staff.IsAvailable);
    }

    [Fact]
    public void An_inactive_staff_member_cannot_be_marked_available()
    {
        var staff = Create(TestRoles.Underwriter, null);
        staff.Deactivate();

        Assert.Equal(UserErrors.InactiveCannotBeAvailable, staff.SetAvailability(true).Error);
        Assert.True(staff.SetAvailability(false).IsSuccess);
    }

    [Fact]
    public void Reactivated_staff_member_can_be_marked_available_again()
    {
        var staff = Create(TestRoles.Underwriter, null);
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
        var staff = Create(TestRoles.Underwriter, null);

        Assert.Equal(UserErrors.InvalidCapacity, staff.ChangeCapacity(capacity).Error);
        Assert.Equal(StaffMember.DefaultCapacity, staff.Capacity);
    }

    // ----- Access snapshot and audit stamps -----

    [Fact]
    public void Access_snapshot_changes_only_with_access_relevant_attributes()
    {
        var staff = Create(TestRoles.Operator, Vake);
        var before = staff.Access;

        staff.Rename("Anna G.");
        staff.ChangeCapacity(12);
        staff.SetAvailability(false);
        Assert.Equal(before, staff.Access);

        staff.ChangeBranch(TestRoles.Operator, Saburtalo);
        Assert.NotEqual(before, staff.Access);
    }

    [Fact]
    public void Deactivation_changes_the_access_snapshot()
    {
        var staff = Create(TestRoles.Underwriter, null);
        var before = staff.Access;

        staff.Deactivate();

        Assert.NotEqual(before, staff.Access);
    }

    [Fact]
    public void Audit_stamps_are_set_through_the_audited_entity_interface()
    {
        var staff = Create(TestRoles.Underwriter, null);
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

    private static StaffMember Create(RoleProfile role, BranchId? branchId) =>
        StaffMember.Create(UserId.New(), "Test User", role, branchId).Value;
}
