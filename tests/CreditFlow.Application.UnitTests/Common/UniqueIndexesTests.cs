using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;

namespace CreditFlow.Application.UnitTests.Common;

public sealed class UniqueIndexesTests
{
    [Fact]
    public void Each_rule_index_becomes_the_business_error_the_service_check_gives()
    {
        Assert.Equal(ApplicationErrors.OpenApplicationExists, UniqueIndexes.ErrorFor(UniqueIndexes.OpenApplicationPerBorrowerAndProduct));
        Assert.Equal(ApplicationErrors.AlreadyDecided, UniqueIndexes.ErrorFor(UniqueIndexes.OneDecisionPerPerson));
        Assert.Equal(ClientErrors.DuplicatePersonalNumber, UniqueIndexes.ErrorFor(UniqueIndexes.ClientPersonalNumber));
    }

    [Theory]
    [InlineData(UniqueIndexes.ApplicationContractNumber)]
    [InlineData(UniqueIndexes.PayoutPerApplication)]
    [InlineData(UniqueIndexes.PayoutIdempotencyKey)]
    [InlineData(UniqueIndexes.LoanPerApplication)]
    [InlineData(UniqueIndexes.LoanContractNumber)]
    [InlineData("UX_Something_New")]
    [InlineData(null)]
    public void Race_only_indexes_and_unknown_ones_are_a_concurrency_conflict(string? index)
    {
        Assert.Equal(CommonErrors.ConcurrencyConflict, UniqueIndexes.ErrorFor(index));
    }
}
