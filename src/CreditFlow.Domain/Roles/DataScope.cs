namespace CreditFlow.Domain.Roles;

/// <summary>Which business records a role may see.</summary>
public enum DataScope
{
    /// <summary>No business data at all (Admin).</summary>
    None = 0,

    /// <summary>Records of the user's branch (Operator).</summary>
    Branch = 3,

    /// <summary>The whole bank.</summary>
    Organization = 5,
}
