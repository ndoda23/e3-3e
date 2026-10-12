namespace CreditFlow.Domain.Clients;

/// <summary>Persistence of the <see cref="Client"/> aggregate.</summary>
public interface IClientRepository
{
    Task<Client?> GetAsync(ClientId id, CancellationToken cancellationToken);

    /// <summary>The given clients, by id (missing ids are left out).</summary>
    Task<IReadOnlyDictionary<ClientId, Client>> GetManyAsync(IEnumerable<ClientId> ids, CancellationToken cancellationToken);

    Task<bool> PersonalNumberExistsAsync(PersonalNumber personalNumber, CancellationToken cancellationToken);

    void Add(Client client);
}
