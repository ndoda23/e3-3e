using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.AuthorityLimits;

/// <summary>Identifier of an authority limit row.</summary>
public readonly record struct AuthorityLimitId(Guid Value)
{
    public static AuthorityLimitId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}
