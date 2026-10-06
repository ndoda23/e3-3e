using Lams.Application.ReferenceData;
using Lams.Application.Users;
using Lams.Domain.Roles;
using Lams.Domain.Users;
using Lams.Infrastructure.Identity;
using Lams.Infrastructure.Persistence;
using Lams.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lams.IntegrationTests.Web;

/// <summary>Runs the real web application against its own database; helpers create users and browser sessions.</summary>
[Collection(SqlServerCollectionDefinition.Name)]
public abstract class WebTestBase(SqlServerContainer sql) : IAsyncLifetime
{
    public const string UserPassword = "Staff!Passw0rd-2026";

    protected LamsWebFactory Factory { get; private set; } = null!;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected virtual IReadOnlyDictionary<string, string?>? Settings => null;

    public ValueTask InitializeAsync()
    {
        Factory = new LamsWebFactory(sql.NewDatabaseConnectionString(), settings: Settings);
        _ = Factory.Server; // start the app (migrations + seeding)
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected WebSession NewSession() => new(Factory.CreateBrowser());

    /// <summary>
    /// Creates a user through the real user administration service and, unless <paramref name="temporary"/> is set,
    /// turns the temporary password into a normal one (<see cref="UserPassword"/>). Returns the user name and id.
    /// </summary>
    protected async Task<(string UserName, UserId Id, string Password)> CreateUserAsync(
        string roleCode,
        string? branchCode = null,
        AuthorityLevel? level = null,
        Segment? segment = null,
        bool temporary = false)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<LamsDbContext>();

        var roleId = await db.Roles.Where(r => r.Code == roleCode).Select(r => r.Id).SingleAsync(Ct);
        var branchId = branchCode is null
            ? (Lams.Domain.Branches.BranchId?)null
            : await db.Branches.Where(b => b.Code == branchCode).Select(b => b.Id).SingleAsync(Ct);

        var userName = $"{roleCode.ToLowerInvariant()}.{Guid.NewGuid():N}"[..20];
        var created = await services.GetRequiredService<UserAdministrationService>().CreateAsync(
            new CreateUserCommand
            {
                UserName = userName,
                Email = $"{userName}@pcb.ge",
                FullName = $"Test {roleCode}",
                RoleId = new RoleId(roleId),
                BranchId = branchId,
                AuthorityLevel = level,
                Segment = segment,
            },
            Ct);
        Assert.True(created.IsSuccess, created.Error?.Description);

        if (temporary)
        {
            return (userName, created.Value.UserId, created.Value.TemporaryPassword);
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(created.Value.UserId.ToString()))!;
        Assert.True((await userManager.ChangePasswordAsync(user, created.Value.TemporaryPassword, UserPassword)).Succeeded);
        user.MustChangePassword = false;
        user.TemporaryPasswordExpiresAt = null;
        Assert.True((await userManager.UpdateAsync(user)).Succeeded);
        return (userName, created.Value.UserId, UserPassword);
    }

    /// <summary>A signed-in System Administrator (a new account, so tests do not share the seeded one).</summary>
    protected async Task<(WebSession Browser, UserId Id)> SignInAsAdministratorAsync()
    {
        var (userName, id, password) = await CreateUserAsync(RoleCodes.SystemAdministrator);
        var browser = NewSession();
        var login = await browser.LoginAsync(userName, password);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, login.StatusCode);
        return (browser, id);
    }

    protected Task<(string UserName, UserId Id, string Password)> CreateSeniorUnderwriterAsync() =>
        CreateUserAsync(RoleCodes.Underwriter, BranchSeedCatalog.Vake, AuthorityLevel.Senior);

    protected async Task<T> QueryAsync<T>(Func<LamsDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LamsDbContext>());
    }
}
