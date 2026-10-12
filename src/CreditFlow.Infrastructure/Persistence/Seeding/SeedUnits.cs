using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.AuthorityLimits;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence.Seeding;

/// <summary>
/// A named, ordered change to reference data. Each unit runs once per database (recorded in <c>SeedHistory</c>)
/// inside its own transaction, and is idempotent so that a partially applied unit can safely run again.
/// After a unit has run, administrators own the data: a unit never runs a second time, so their changes stay.
/// </summary>
internal interface ISeedUnit
{
    /// <summary>Ordered id: <c>NNNN_snake_case_description</c>. Never rename or reuse an id.</summary>
    string Id { get; }

    Task ApplyAsync(CreditFlowDbContext db, CancellationToken cancellationToken);
}

/// <summary>
/// The seed units, in the order they are applied. To change reference data (new role, new permission grant,
/// new branch, new limit), APPEND a new unit; never edit a unit that may already have run somewhere.
/// See README, "Reference data and seeding".
/// </summary>
internal static class SeedUnits
{
    public static IReadOnlyList<ISeedUnit> All { get; } =
    [
        new InitialRolesAndPermissions(),
        new InitialBranches(),
        new InitialAuthorityLimits(),
    ];

    /// <summary>The roles created by the first unit. Roles added later get their own unit.</summary>
    internal static readonly string[] InitialRoleCodes =
    [
        RoleCodes.LoanOfficer, RoleCodes.Underwriter, RoleCodes.BranchManager, RoleCodes.BusinessDevelopmentManager,
        RoleCodes.BackOffice, RoleCodes.AmlAnalyst, RoleCodes.FinancialAnalysis, RoleCodes.Head, RoleCodes.Director,
        RoleCodes.SystemAdministrator,
    ];

    private sealed class InitialRolesAndPermissions : ISeedUnit
    {
        public string Id => "0001_initial_roles_permissions";

        public async Task ApplyAsync(CreditFlowDbContext db, CancellationToken cancellationToken)
        {
            foreach (var code in InitialRoleCodes)
            {
                var seed = RoleSeedCatalog.Get(code);
                await SeedOperations.CreateRoleIfMissingAsync(db, seed, seed.Permissions, cancellationToken);
            }
        }
    }

    private sealed class InitialBranches : ISeedUnit
    {
        public string Id => "0002_initial_branches";

        public async Task ApplyAsync(CreditFlowDbContext db, CancellationToken cancellationToken)
        {
            foreach (var seed in BranchSeedCatalog.All)
            {
                await SeedOperations.AddBranchIfMissingAsync(db, seed, cancellationToken);
            }
        }
    }

    private sealed class InitialAuthorityLimits : ISeedUnit
    {
        public string Id => "0003_initial_authority_limits";

        public async Task ApplyAsync(CreditFlowDbContext db, CancellationToken cancellationToken)
        {
            foreach (var seed in AuthorityLimitSeedCatalog.All)
            {
                await SeedOperations.AddAuthorityLimitIfMissingAsync(db, seed, cancellationToken);
            }
        }
    }
}

/// <summary>Idempotent building blocks for seed units.</summary>
internal static class SeedOperations
{
    /// <summary>Creates the role with its fixed definition and the given permissions, unless it already exists.</summary>
    public static async Task CreateRoleIfMissingAsync(
        CreditFlowDbContext db, RoleSeed seed, IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        if (await db.Roles.AnyAsync(r => r.Code == seed.Code, cancellationToken))
        {
            return;
        }

        var role = new ApplicationRole
        {
            Id = RoleId.New().Value,
            Name = seed.Code,
            NormalizedName = seed.Code.ToUpperInvariant(),
            Code = seed.Code,
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
        ApplyFixedDefinition(role, seed);
        role.Permissions.AddRange(permissions.Distinct(StringComparer.Ordinal)
            .Select(p => new RolePermission { RoleId = role.Id, Permission = p }));
        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Grants permissions to an existing role (skips those it already holds).</summary>
    public static async Task GrantAsync(
        CreditFlowDbContext db, string roleCode, IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        var role = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Code == roleCode, cancellationToken);
        var requested = permissions.Distinct(StringComparer.Ordinal).ToList();

        // A seed unit must respect the same rules as the role editor: known permissions, segregation of duties.
        var unknown = requested.FirstOrDefault(p => !CreditFlow.Application.Authorization.PermissionCatalog.Contains(p));
        if (unknown is not null)
        {
            throw new InvalidOperationException($"Seed grants unknown permission '{unknown}' to role {roleCode}.");
        }

        var combined = PermissionSet.Create(
            role.IsAdministrative, role.Permissions.Select(p => p.Permission).Concat(requested));
        if (combined.IsFailure)
        {
            throw new InvalidOperationException($"Seed grant to role {roleCode} is invalid: {combined.Error!.Description}");
        }

        foreach (var permission in requested.Where(p => role.Permissions.All(rp => rp.Permission != p)))
        {
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task AddBranchIfMissingAsync(CreditFlowDbContext db, BranchSeed seed, CancellationToken cancellationToken)
    {
        if (!await db.Branches.AnyAsync(b => b.Code == seed.Code, cancellationToken))
        {
            db.Branches.Add(Branch.Create(seed.Code, seed.Name, seed.City).Value);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public static async Task AddAuthorityLimitIfMissingAsync(
        CreditFlowDbContext db, AuthorityLimitSeed seed, CancellationToken cancellationToken)
    {
        var roleId = new RoleId(await db.Roles.Where(r => r.Code == seed.RoleCode).Select(r => r.Id).SingleAsync(cancellationToken));

        var exists = await db.AuthorityLimits.AnyAsync(
            l => l.RoleId == roleId
                 && l.LimitType == seed.LimitType
                 && l.AuthorityLevel == seed.AuthorityLevel
                 && l.Currency == seed.Currency,
            cancellationToken);
        if (exists)
        {
            return;
        }

        var limit = seed.LimitType == LimitType.CreditDecision
            ? AuthorityLimit.CreateCreditDecision(roleId, seed.AuthorityLevel, seed.Currency!.Value, seed.MaxAmount!.Value)
            : AuthorityLimit.CreatePricingException(roleId, seed.MaxRateGapPp, seed.MinCustomerRoaePct);
        db.AuthorityLimits.Add(limit.Value);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The code-owned part of a role, re-applied on every start (never permissions or the MFA flag).</summary>
    public static void ApplyFixedDefinition(ApplicationRole role, RoleSeed seed)
    {
        role.DisplayName = seed.DisplayName;
        role.Description = seed.Description;
        role.DataScope = seed.DataScope;
        role.Dashboard = seed.Dashboard;
        role.BranchRule = seed.BranchRule;
        role.SegmentRule = seed.SegmentRule;
        role.AuthorityLevelRule = seed.AuthorityLevelRule;
        role.IsAdministrative = seed.IsAdministrative;
        role.IsSystem = true;
    }
}
