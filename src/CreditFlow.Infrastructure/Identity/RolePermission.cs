using System.Diagnostics.CodeAnalysis;

namespace CreditFlow.Infrastructure.Identity;

/// <summary>One permission granted to a role.</summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Entity name defined by the CreditFlow data model (RolePermission(RoleId, Permission)).")]
public sealed class RolePermission
{
    public Guid RoleId { get; set; }

    public string Permission { get; set; } = string.Empty;
}
