using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.AuthorityLimits;

public interface IAuthorityLimitRepository
{
    Task<IReadOnlyList<AuthorityLimit>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AuthorityLimit>> ListForRoleAsync(RoleId roleId, CancellationToken cancellationToken);

    Task<AuthorityLimit?> GetAsync(AuthorityLimitId id, CancellationToken cancellationToken);

    Task<bool> CreditDecisionLimitExistsAsync(
        RoleId roleId, AuthorityLevel? authorityLevel, Currency currency, CancellationToken cancellationToken);

    Task<bool> PricingExceptionLimitExistsAsync(RoleId roleId, CancellationToken cancellationToken);

    void Add(AuthorityLimit limit);

    void Remove(AuthorityLimit limit);
}
