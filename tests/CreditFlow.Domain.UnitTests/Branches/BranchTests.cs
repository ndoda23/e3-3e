using CreditFlow.Domain.Branches;

namespace CreditFlow.Domain.UnitTests.Branches;

public sealed class BranchTests
{
    [Fact]
    public void A_new_branch_is_active_and_normalized()
    {
        var result = Branch.Create(" vak ", " Vake ", " Tbilisi ");

        Assert.True(result.IsSuccess);
        Assert.Equal("VAK", result.Value.Code);
        Assert.Equal("Vake", result.Value.Name);
        Assert.Equal("Tbilisi", result.Value.City);
        Assert.True(result.Value.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("V")]
    [InlineData("TOO-LONG-CODE")]
    [InlineData("V K")]
    public void Invalid_codes_are_rejected(string code)
    {
        Assert.Equal(BranchErrors.InvalidCode, Branch.Create(code, "Vake", "Tbilisi").Error);
    }

    [Fact]
    public void Name_and_city_are_required()
    {
        Assert.Equal(BranchErrors.InvalidName, Branch.Create("VAK", " ", "Tbilisi").Error);
        Assert.Equal(BranchErrors.InvalidCity, Branch.Create("VAK", "Vake", null).Error);
    }

    [Fact]
    public void Update_changes_name_and_city_but_not_code()
    {
        var branch = Branch.Create("VAK", "Vake", "Tbilisi").Value;

        var result = branch.Update("Vake Plaza", "Tbilisi");

        Assert.True(result.IsSuccess);
        Assert.Equal("Vake Plaza", branch.Name);
        Assert.Equal("VAK", branch.Code);
    }

    [Fact]
    public void Failed_update_leaves_the_branch_unchanged()
    {
        var branch = Branch.Create("VAK", "Vake", "Tbilisi").Value;

        var result = branch.Update(new string('x', Branch.NameMaxLength + 1), "Tbilisi");

        Assert.True(result.IsFailure);
        Assert.Equal("Vake", branch.Name);
    }

    [Fact]
    public void Branch_can_be_deactivated_and_reactivated()
    {
        var branch = Branch.Create("VAK", "Vake", "Tbilisi").Value;

        branch.Deactivate();
        Assert.False(branch.IsActive);

        branch.Activate();
        Assert.True(branch.IsActive);
    }

    [Fact]
    public void Branch_with_assigned_users_cannot_be_deleted()
    {
        Assert.Equal(BranchErrors.HasAssignedUsers, Branch.EnsureCanBeDeleted(1).Error);
        Assert.True(Branch.EnsureCanBeDeleted(0).IsSuccess);
    }
}
