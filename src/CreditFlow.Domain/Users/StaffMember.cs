using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;

namespace CreditFlow.Domain.Users;

/// <summary>
/// A bank employee who uses CreditFlow: the business side of a user account. It shares its <see cref="Id"/> with the
/// authentication account (1:1) and is the entity that business records reference.
/// <para>
/// A staff member holds exactly one role (<see cref="RoleId"/>, a single value: segregation of duties).
/// The role governs the attributes: required attributes must be set, and attributes the role does not allow are
/// cleared (choosing the System Administrator role clears branch, segment and authority level).
/// Staff members are never deleted, only deactivated.
/// </para>
/// </summary>
public sealed class StaffMember : IAuditedEntity
{
    public const int FullNameMaxLength = 200;
    public const int MaxCapacity = 100;
    public const int DefaultCapacity = 10;

    private StaffMember()
    {
    }

    public UserId Id { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public RoleId RoleId { get; private set; }

    public BranchId? BranchId { get; private set; }

    public Segment? Segment { get; private set; }

    public AuthorityLevel? AuthorityLevel { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Working today (true) or unavailable (false), as on the Team screen.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Maximum number of open files the staff member should carry at once.</summary>
    public int Capacity { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public UserId? UpdatedBy { get; private set; }

    /// <summary>Snapshot of the access-relevant state; compare before and after an edit.</summary>
    public StaffAccess Access => new(RoleId, BranchId, Segment, AuthorityLevel, IsActive);

    public static Result<StaffMember> Create(
        UserId id,
        string? fullName,
        RoleProfile role,
        BranchId? branchId,
        Segment? segment,
        AuthorityLevel? authorityLevel,
        int capacity = DefaultCapacity)
    {
        var staffMember = new StaffMember { Id = id, IsActive = true, IsAvailable = true };

        var result = staffMember.Rename(fullName);
        if (result.IsSuccess)
        {
            result = staffMember.ChangeCapacity(capacity);
        }

        if (result.IsSuccess)
        {
            result = staffMember.AssignRole(role, branchId, segment, authorityLevel);
        }

        return result.IsSuccess ? Result.Success(staffMember) : Result.Failure<StaffMember>(result.Error!);
    }

    /// <summary>All attribute errors for the given role, so a form can show every field error at once.</summary>
    public static IReadOnlyList<Error> ValidateAssignment(
        RoleProfile role, BranchId? branchId, Segment? segment, AuthorityLevel? authorityLevel)
    {
        ArgumentNullException.ThrowIfNull(role);

        var errors = new List<Error>();

        if (role.BranchRule == AttributeRule.Required && branchId is null)
        {
            errors.Add(UserErrors.BranchRequired);
        }

        if (segment is { } s && !Enum.IsDefined(s))
        {
            errors.Add(UserErrors.InvalidSegment);
        }
        else if (role.SegmentRule == AttributeRule.Required && segment is null)
        {
            errors.Add(UserErrors.SegmentRequired);
        }

        if (authorityLevel is { } level && !Enum.IsDefined(level))
        {
            errors.Add(UserErrors.InvalidAuthorityLevel);
        }
        else if (role.AuthorityLevelRule == AttributeRule.Required && authorityLevel is null)
        {
            errors.Add(UserErrors.AuthorityLevelRequired);
        }

        return errors;
    }

    public Result Rename(string? fullName)
    {
        var name = Text.Normalize(fullName);
        if (name.Length is 0 or > FullNameMaxLength)
        {
            return Result.Failure(UserErrors.InvalidFullName);
        }

        FullName = name;
        return Result.Success();
    }

    /// <summary>
    /// Gives the staff member a role, replacing the previous one, together with the attributes that role governs.
    /// Guard rails that need other users (self-demotion, last administrator) are checked by
    /// <see cref="AdminContinuityPolicy"/> before calling this.
    /// </summary>
    public Result AssignRole(RoleProfile role, BranchId? branchId, Segment? segment, AuthorityLevel? authorityLevel)
    {
        var errors = ValidateAssignment(role, branchId, segment, authorityLevel);
        if (errors.Count > 0)
        {
            return Result.Failure(errors[0]);
        }

        RoleId = role.Id;
        BranchId = Keep(role.BranchRule, branchId);
        Segment = Keep(role.SegmentRule, segment);
        AuthorityLevel = Keep(role.AuthorityLevelRule, authorityLevel);
        return Result.Success();
    }

    /// <param name="currentRole">Profile of the staff member's current role.</param>
    /// <param name="branchId">The new branch.</param>
    public Result ChangeBranch(RoleProfile currentRole, BranchId? branchId)
    {
        EnsureCurrentRole(currentRole);
        return AssignRole(currentRole, branchId, Segment, AuthorityLevel);
    }

    /// <param name="currentRole">Profile of the staff member's current role.</param>
    /// <param name="segment">The new segment.</param>
    public Result ChangeSegment(RoleProfile currentRole, Segment? segment)
    {
        EnsureCurrentRole(currentRole);
        return AssignRole(currentRole, BranchId, segment, AuthorityLevel);
    }

    /// <param name="currentRole">Profile of the staff member's current role.</param>
    /// <param name="authorityLevel">The new authority level.</param>
    public Result ChangeAuthorityLevel(RoleProfile currentRole, AuthorityLevel? authorityLevel)
    {
        EnsureCurrentRole(currentRole);
        return AssignRole(currentRole, BranchId, Segment, authorityLevel);
    }

    public Result ChangeCapacity(int capacity)
    {
        if (capacity is < 0 or > MaxCapacity)
        {
            return Result.Failure(UserErrors.InvalidCapacity);
        }

        Capacity = capacity;
        return Result.Success();
    }

    public Result Activate()
    {
        IsActive = true;
        return Result.Success();
    }

    /// <summary>
    /// Deactivates the staff member (they can no longer sign in) and marks them unavailable.
    /// Check <see cref="AdminContinuityPolicy.EnsureCanDeactivate"/> first.
    /// </summary>
    public Result Deactivate()
    {
        IsActive = false;
        IsAvailable = false;
        return Result.Success();
    }

    public Result SetAvailability(bool isAvailable)
    {
        if (isAvailable && !IsActive)
        {
            return Result.Failure(UserErrors.InactiveCannotBeAvailable);
        }

        IsAvailable = isAvailable;
        return Result.Success();
    }

    void IAuditedEntity.MarkCreated(UserId? by, DateTimeOffset at)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    void IAuditedEntity.MarkUpdated(UserId? by, DateTimeOffset at)
    {
        UpdatedAt = at;
        UpdatedBy = by;
    }

    private void EnsureCurrentRole(RoleProfile role)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (role.Id != RoleId)
        {
            throw new InvalidOperationException(
                $"Role profile {role.Id} is not the current role {RoleId} of staff member {Id}.");
        }
    }

    private static T? Keep<T>(AttributeRule rule, T? value)
        where T : struct =>
        rule == AttributeRule.NotAllowed ? null : value;
}
