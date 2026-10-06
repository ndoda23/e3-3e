namespace Lams.Domain.Roles;

/// <summary>Whether a user attribute (branch, segment, authority level) is required, optional or not allowed for a role.</summary>
public enum AttributeRule
{
    /// <summary>The attribute is cleared for users of the role.</summary>
    NotAllowed = 0,

    /// <summary>The attribute may be set.</summary>
    Optional = 1,

    /// <summary>The attribute must be set.</summary>
    Required = 2,
}
