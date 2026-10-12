using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Branches;

/// <summary>Identifier of a branch.</summary>
public readonly record struct BranchId(Guid Value)
{
    public static BranchId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}
