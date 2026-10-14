using CreditFlow.Domain.Applications;

namespace CreditFlow.Domain.UnitTests.Applications;

/// <summary>The application reference people use: <c>APP-</c> and the number from the database sequence.</summary>
public sealed class ApplicationReferenceTests
{
    [Theory]
    [InlineData(1, "APP-000001")]
    [InlineData(123, "APP-000123")]
    [InlineData(1_234_567, "APP-1234567")]
    public void The_reference_is_the_padded_number(long number, string expected)
    {
        Assert.Equal(expected, LoanApplication.FormatReference(number));
        Assert.Equal(number, LoanApplication.ParseReference(expected));
    }

    [Theory]
    [InlineData("app-123", 123L)]
    [InlineData("  APP-000042 ", 42L)]
    [InlineData("APP-0", null)]
    [InlineData("APP-", null)]
    [InlineData("APP-12a", null)]
    [InlineData("LN-2026-000123", null)]
    [InlineData("01001011001", null)]
    [InlineData(null, null)]
    public void Typed_references_are_recognised(string? text, long? expected)
    {
        Assert.Equal(expected, LoanApplication.ParseReference(text));
    }

    [Fact]
    public void Two_applications_created_in_the_same_millisecond_still_differ_in_their_ids_tail_not_head()
    {
        // The id is a version 7 GUID: its head is the timestamp, so it can never serve as a reference.
        var at = DateTimeOffset.UtcNow;
        var first = Guid.CreateVersion7(at).ToString("N");
        var second = Guid.CreateVersion7(at).ToString("N");

        Assert.Equal(first[..12], second[..12]);
        Assert.NotEqual(first, second);
    }
}
