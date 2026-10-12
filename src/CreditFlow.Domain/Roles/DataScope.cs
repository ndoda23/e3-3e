namespace CreditFlow.Domain.Roles;

/// <summary>Which records a role may see.</summary>
public enum DataScope
{
    /// <summary>No business data at all (System Administrator).</summary>
    None = 0,

    /// <summary>Records the user owns (created).</summary>
    Own = 1,

    /// <summary>Records assigned to the user.</summary>
    Assigned = 2,

    /// <summary>Records of the user's branch.</summary>
    Branch = 3,

    /// <summary>Records of the user's segment (Retail / Business).</summary>
    Segment = 4,

    /// <summary>The whole bank.</summary>
    Organization = 5,
}
