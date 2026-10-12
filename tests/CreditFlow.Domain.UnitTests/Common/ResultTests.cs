using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.UnitTests.Common;

public sealed class ResultTests
{
    private static readonly Error SomeError = Error.Validation("Test.Error", "Something is wrong.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var result = Result.Failure(SomeError);

        Assert.True(result.IsFailure);
        Assert.Same(SomeError, result.Error);
    }

    [Fact]
    public void Success_with_value_exposes_the_value()
    {
        var result = Result.Success(42);

        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        var result = Result.Failure<int>(SomeError);

        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("Test.Error", exception.Message, StringComparison.Ordinal);
    }
}
