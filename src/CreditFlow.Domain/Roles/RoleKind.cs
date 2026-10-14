namespace CreditFlow.Domain.Roles;

/// <summary>
/// Segregation-of-duties class of a role. It decides which permissions the role may hold
/// (see <see cref="PermissionSet"/>), so it is enforced for seeded roles and for every edit in the role editor.
/// </summary>
public enum RoleKind
{
    /// <summary>Works with business data; may hold any non-administrative permission.</summary>
    Business = 1,

    /// <summary>Reads business data only (Auditor); may hold only read-only, non-administrative permissions.</summary>
    ReadOnly = 2,

    /// <summary>Administers the system; may hold only <c>admin.*</c> permissions and sees no business data.</summary>
    Administrative = 3,
}
