using Lams.Domain.Common;

namespace Lams.Domain.Roles;

/// <summary>Identifier of a role. Matches the Identity role key.</summary>
public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}
