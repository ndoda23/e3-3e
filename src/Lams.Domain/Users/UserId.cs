using Lams.Domain.Common;

namespace Lams.Domain.Users;

/// <summary>Identifier of a user. Matches the Identity user key.</summary>
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}
