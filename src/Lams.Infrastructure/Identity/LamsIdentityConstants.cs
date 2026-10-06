namespace Lams.Infrastructure.Identity;

/// <summary>Claim types issued in the authentication cookie. Only stable identity data; never permissions.</summary>
public static class LamsClaimTypes
{
    /// <summary>The id of the user's (single) role. Informational; authorization reads the cached access profile.</summary>
    public const string RoleId = "lams:role_id";
}

/// <summary>Keys stored in the authentication properties of the cookie.</summary>
public static class LamsSessionProperties
{
    /// <summary>
    /// When the session originally started (round-trip "O" format). Preserved across cookie re-issues;
    /// used to enforce the absolute session lifetime.
    /// </summary>
    public const string SignedInAt = "lams.issued";
}
