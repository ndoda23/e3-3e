using Microsoft.AspNetCore.Identity;

namespace CreditFlow.Infrastructure.Identity;

/// <summary>
/// The authentication account. It holds ONLY authentication data; all business data (name, role, branch, segment,
/// authority level, active flag, availability, capacity) lives on the domain <c>StaffMember</c>, which shares this id.
/// External logins (Active Directory / Entra ID) can later be linked through <c>AspNetUserLogins</c> to the same account.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>The user must enrol in MFA even if their role does not require it.</summary>
    public bool RequireMfa { get; set; }

    /// <summary>The user signed in with a temporary password and must change it before doing anything else.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>When the current temporary password stops working; <c>null</c> when the password is not temporary.</summary>
    public DateTimeOffset? TemporaryPasswordExpiresAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}
