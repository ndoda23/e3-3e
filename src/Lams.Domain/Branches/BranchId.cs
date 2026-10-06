using Lams.Domain.Common;

namespace Lams.Domain.Branches;

/// <summary>Identifier of a branch.</summary>
public readonly record struct BranchId(Guid Value)
{
    public static BranchId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}
