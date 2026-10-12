using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Users;

/// <summary>Identifier of a user. Matches the Identity user key.</summary>
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}
