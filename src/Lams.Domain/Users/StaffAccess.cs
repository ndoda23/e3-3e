using Lams.Domain.Branches;
using Lams.Domain.Roles;

namespace Lams.Domain.Users;

/// <summary>
/// The access-relevant state of a staff member. When it changes, the user's cached access profile must be
/// invalidated and their security stamp updated (compare a snapshot taken before and after an edit).
/// </summary>
public sealed record StaffAccess(
    RoleId RoleId,
    BranchId? BranchId,
    Segment? Segment,
    AuthorityLevel? AuthorityLevel,
    bool IsActive);
