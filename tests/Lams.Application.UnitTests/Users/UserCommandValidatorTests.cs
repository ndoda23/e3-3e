using Lams.Application.Users;
using Lams.Domain.Roles;
using Lams.Domain.Users;

namespace Lams.Application.UnitTests.Users;

public sealed class UserCommandValidatorTests
{
    private readonly CreateUserCommandValidator _validator = new();

    private static CreateUserCommand Valid() => new()
    {
        UserName = "nika.beridze",
        Email = "nika.b@pcb.ge",
        FullName = "Nika Beridze",
        RoleId = RoleId.New(),
    };

    [Fact]
    public void A_valid_command_passes()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("nika beridze")]
    [InlineData("nika@beridze")]
    public void Invalid_user_names_fail(string userName)
    {
        var result = _validator.Validate(Valid() with { UserName = userName });

        Assert.Contains(result.Errors, e => e.ErrorCode == UserFieldRules.InvalidUserNameCode);
    }

    [Fact]
    public void Invalid_email_fails()
    {
        var result = _validator.Validate(Valid() with { Email = "not-an-email" });

        Assert.Contains(result.Errors, e => e.ErrorCode == UserFieldRules.InvalidEmailCode);
    }

    [Fact]
    public void Role_is_required()
    {
        var result = _validator.Validate(Valid() with { RoleId = default });

        Assert.Contains(result.Errors, e => e.ErrorCode == UserErrors.RoleRequired.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Capacity_must_be_in_range(int capacity)
    {
        var result = _validator.Validate(Valid() with { Capacity = capacity });

        Assert.Contains(result.Errors, e => e.ErrorCode == UserErrors.InvalidCapacity.Code);
    }
}
