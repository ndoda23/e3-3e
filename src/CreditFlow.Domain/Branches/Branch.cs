using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Branches;

/// <summary>A bank branch. Branches are deactivated rather than deleted once users are assigned to them.</summary>
public sealed class Branch
{
    public const int CodeMaxLength = 10;
    public const int NameMaxLength = 100;
    public const int CityMaxLength = 100;

    private Branch()
    {
    }

    public BranchId Id { get; private set; }

    /// <summary>Short unique code (e.g. <c>VAK</c>). Immutable after creation.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public static Result<Branch> Create(string? code, string? name, string? city)
    {
        var normalizedCode = Text.Normalize(code).ToUpperInvariant();
        if (normalizedCode.Length is < 2 or > CodeMaxLength
            || !normalizedCode.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            return Result.Failure<Branch>(BranchErrors.InvalidCode);
        }

        var branch = new Branch { Id = BranchId.New(), Code = normalizedCode, IsActive = true };
        var details = branch.Update(name, city);
        return details.IsSuccess ? Result.Success(branch) : Result.Failure<Branch>(details.Error!);
    }

    public Result Update(string? name, string? city)
    {
        var normalizedName = Text.Normalize(name);
        var normalizedCity = Text.Normalize(city);

        if (normalizedName.Length is 0 or > NameMaxLength)
        {
            return Result.Failure(BranchErrors.InvalidName);
        }

        if (normalizedCity.Length is 0 or > CityMaxLength)
        {
            return Result.Failure(BranchErrors.InvalidCity);
        }

        Name = normalizedName;
        City = normalizedCity;
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>A branch can be deleted only while no user (active or not) is assigned to it.</summary>
    public static Result EnsureCanBeDeleted(int assignedUserCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assignedUserCount);
        return assignedUserCount == 0 ? Result.Success() : Result.Failure(BranchErrors.HasAssignedUsers);
    }
}
