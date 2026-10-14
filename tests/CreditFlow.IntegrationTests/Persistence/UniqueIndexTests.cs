using System.Reflection;
using CreditFlow.Application.Applications;
using CreditFlow.Application.Clients;
using CreditFlow.Application.Common;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace CreditFlow.IntegrationTests.Persistence;

/// <summary>
/// The database's unique indexes are the last line behind the services' own checks. A second request that slips
/// between check and save (staged here by <see cref="SaveRace"/>) gets the same business error as the check gives,
/// never an unhandled <see cref="DbUpdateException"/> (a 500 in the browser).
/// </summary>
public sealed class UniqueIndexTests(SqlServerContainer sql) : DatabaseTestBase(sql)
{
    private const string VakeOperator = "anna.gelashvili";
    private const string CommitteeMember = "irakli.tsereteli";

    private readonly FakeTimeProvider _clock = new(BankTime.ToInstant(new DateOnly(2026, 10, 12), new TimeOnly(10, 0)));

    protected override string Environment => "Development";

    protected override bool SeedDevUsers => true;

    protected override TimeProvider? Clock => _clock;

    private SaveRace Race => Host.Services.GetRequiredService<SaveRace>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        // The real unit of work, with a hook that lets another request commit just before this one saves.
        var real = services.Single(d => d.ServiceType == typeof(IUnitOfWork)).ImplementationType!;
        services.RemoveAll<IUnitOfWork>();
        services.Add(new ServiceDescriptor(real, real, ServiceLifetime.Scoped));
        services.AddSingleton<SaveRace>();
        services.AddScoped<IUnitOfWork>(sp => new RacingUnitOfWork((IUnitOfWork)sp.GetRequiredService(real), sp.GetRequiredService<SaveRace>()));
    }

    [Fact]
    public async Task Every_mapped_unique_index_exists_with_that_name()
    {
        var names = typeof(UniqueIndexes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        var unique = await Host.QueryAsync(db => Task.FromResult(db.Model.GetEntityTypes()
            .SelectMany(t => t.GetIndexes()).Where(i => i.IsUnique).Select(i => i.GetDatabaseName()).ToHashSet()));

        Assert.Equal(8, names.Count);
        Assert.All(names, name => Assert.Contains(name, unique));
    }

    [Fact]
    public async Task A_duplicate_key_is_reported_with_the_index_it_violated()
    {
        var number = new TestLending(Host.Services, _clock).NextPersonalNumber();
        await new TestLending(Host.Services, _clock).ClientAsync(number);

        var violation = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => Host.InScopeAsync(async sp =>
        {
            sp.GetRequiredService<CreditFlowDbContext>().Clients.Add(NewClient(number));
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }));

        Assert.Equal(UniqueIndexes.ClientPersonalNumber, violation.IndexName);
    }

    [Fact]
    public async Task A_second_open_application_for_the_same_product_names_the_first()
    {
        var anna = await UserAsync(VakeOperator);
        var command = await DraftCommandAsync();
        var first = await CreateAsync(anna, command);
        Assert.True(first.IsSuccess, first.Error?.Description);

        var second = await CreateAsync(anna, command);

        Assert.Equal(ApplicationErrors.OpenApplicationExists.Code, second.Error!.Code);
        Assert.Contains(await ReferenceAsync(first.Value), second.Error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_parallel_create_that_wins_the_race_is_named_instead_of_a_500()
    {
        var anna = await UserAsync(VakeOperator);
        var command = await DraftCommandAsync();
        Result<LoanApplicationId>? winner = null;
        Race.Before = async () => winner = await CreateAsync(anna, command);

        var loser = await CreateAsync(anna, command);

        Assert.True(winner!.IsSuccess, winner.Error?.Description);
        Assert.Equal(ApplicationErrors.OpenApplicationExists.Code, loser.Error!.Code);
        Assert.Contains(await ReferenceAsync(winner.Value), loser.Error.Description, StringComparison.Ordinal);
        Assert.Equal(1, await Host.QueryAsync(db => db.LoanApplications.CountAsync(a => a.BorrowerId == command.BorrowerId, Ct)));
    }

    [Fact]
    public async Task A_parallel_registration_of_the_same_personal_number_is_a_duplicate()
    {
        var anna = await UserAsync(VakeOperator);
        var lending = new TestLending(Host.Services, _clock);
        var number = lending.NextPersonalNumber();
        Race.Before = () => lending.ClientAsync(number);

        var result = await Host.InScopeAsAsync(anna, sp => sp.GetRequiredService<ClientService>().RegisterAsync(
            new RegisterClientCommand(
                number, new ClientDetails("Eka", "Tsiklauri", TestLending.BirthDate, "+995 555 123456", null, "Tbilisi"),
                IdentityDocumentType.IdCard, "ID" + number, new DateOnly(2020, 1, 1), new DateOnly(2030, 1, 1)),
            Ct));

        Assert.Equal(ClientErrors.DuplicatePersonalNumber.Code, result.Error!.Code);
    }

    [Fact]
    public async Task A_member_voting_twice_in_parallel_counts_once()
    {
        var member = await UserAsync(CommitteeMember);
        var id = await new TestLending(Host.Services, _clock).InReviewAsync(LendingSeedCatalog.Mortgage, 150_000m, await UserAsync(VakeOperator));
        Result? first = null;
        Race.Before = async () => first = await VoteAsync(member, id);

        var second = await VoteAsync(member, id);

        Assert.True(first!.IsSuccess, first.Error?.Description);
        Assert.Equal(ApplicationErrors.AlreadyDecided, second.Error);
        Assert.Equal(1, await Host.QueryAsync(db => db.LoanApplications.Where(a => a.Id == id)
            .SelectMany(a => a.Decisions).CountAsync(d => d.DecidedBy == member, Ct)));
    }

    private Task<Result> VoteAsync(UserId member, LoanApplicationId id) =>
        Host.InScopeAsAsync(member, sp => sp.GetRequiredService<DecisionService>().VoteAsync(id, true, null, Ct));

    private Task<Result<LoanApplicationId>> CreateAsync(UserId user, CreateApplicationCommand command) =>
        Host.InScopeAsAsync(user, sp => sp.GetRequiredService<ApplicationService>().CreateAsync(command, Ct));

    private async Task<CreateApplicationCommand> DraftCommandAsync()
    {
        var borrower = await new TestLending(Host.Services, _clock).ClientAsync();
        var product = await Host.QueryAsync(db => db.LoanProducts.Where(p => p.Code == LendingSeedCatalog.Consumer).Select(p => p.Id).SingleAsync(Ct));
        return new CreateApplicationCommand(borrower, product, 5_000m, Currency.GEL, 24, 18m, ScheduleType.Annuity);
    }

    private Task<string> ReferenceAsync(LoanApplicationId id) =>
        Host.QueryAsync(async db => LoanApplication.FormatReference(
            await db.LoanApplications.Where(a => a.Id == id).Select(a => a.Number).SingleAsync(Ct)));

    private Task<UserId> UserAsync(string userName) =>
        Host.QueryAsync(async db => new UserId(await db.UserDirectory.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(Ct)));

    private Client NewClient(string number) => Client.Register(
        PersonalNumber.Create(number).Value,
        new ClientDetails("Test", "Client", TestLending.BirthDate, "+995 555 000000", null, "Tbilisi"),
        IdentityDocument.Create(IdentityDocumentType.IdCard, "ID" + number, new DateOnly(2020, 1, 1), new DateOnly(2035, 1, 1)).Value,
        BankTime.DateOf(_clock.GetUtcNow())).Value;
}

/// <summary>Lets "another request" commit once, right before the next save — between a service's check and its write.</summary>
public sealed class SaveRace
{
    public Func<Task>? Before { get; set; }

    public async Task RunAsync()
    {
        var before = Before;
        Before = null; // the racing request saves through the same unit of work type: run once
        if (before is not null)
        {
            await before();
        }
    }
}

internal sealed class RacingUnitOfWork(IUnitOfWork inner, SaveRace race) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await race.RunAsync();
        await inner.SaveChangesAsync(cancellationToken);
    }

    public void Reset() => inner.Reset();

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
        where TResult : Result =>
        inner.ExecuteInTransactionAsync(work, cancellationToken);
}
