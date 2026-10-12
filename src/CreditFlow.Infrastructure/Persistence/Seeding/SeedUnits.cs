using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
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
/// new branch), APPEND a new unit; never edit a unit that may already have run somewhere. A unit may be
/// <em>retired</em> (kept in the list, doing nothing) only when a later unit replaces everything it created.
/// See README, "Reference data and seeding".
/// </summary>
internal static class SeedUnits
{
    public static IReadOnlyList<ISeedUnit> All { get; } =
    [
        new RetiredUnit("0001_initial_roles_permissions"),
        new InitialBranches(),
        new RetiredUnit("0003_initial_authority_limits"),
        new CreditFlowRoles(),
    ];

    /// <summary>
    /// A unit whose data was replaced by a later unit (the LAMS roles and authority limits, replaced by
    /// <see cref="CreditFlowRoles"/>). Its id stays so that the history of existing databases remains consistent.
    /// </summary>
    private sealed class RetiredUnit(string id) : ISeedUnit
    {
        public string Id => id;

        public Task ApplyAsync(CreditFlowDbContext db, CancellationToken cancellationToken) => Task.CompletedTask;
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

    /// <summary>
    /// Replaces the LAMS roles with the roles of the CreditFlow task. On a fresh database it simply creates the seven
    /// roles. On a database that had the LAMS roles, it also keeps the records of the roles whose code survives
    /// (<c>UW</c>, <c>SYSADMIN</c>) with the new permission matrix, moves Loan Officers to Operator, moves the users of
    /// every other LAMS role to Auditor and deactivates them (fail closed), and deletes the LAMS roles.
    /// </summary>
    private sealed class CreditFlowRoles : ISeedUnit
    {
        /// <summary>The LAMS role codes this unit replaces (frozen history, not reference data).</summary>
        private const string LamsLoanOfficer = "LO";

        private static readonly string[] OtherLamsRoles = ["BM", "BDM", "BO", "AML", "FAB", "HD", "DIR"];

        public string Id => "0004_creditflow_roles";

        public async Task ApplyAsync(CreditFlowDbContext db, CancellationToken cancellationToken)
        {
            foreach (var seed in RoleSeedCatalog.All)
            {
                await SeedOperations.CreateRoleIfMissingAsync(db, seed, seed.Permissions, cancellationToken);
                await SeedOperations.ReplacePermissionsAsync(db, seed, cancellationToken);
            }

            await MoveStaffAsync(db, LamsLoanOfficer, RoleCodes.Operator, deactivate: false, cancellationToken);
            foreach (var code in OtherLamsRoles)
            {
                await MoveStaffAsync(db, code, RoleCodes.Auditor, deactivate: true, cancellationToken);
            }

            var lamsRoles = await db.Roles
                .Include(r => r.Permissions)
                .Where(r => r.Code == LamsLoanOfficer || OtherLamsRoles.Contains(r.Code))
                .ToListAsync(cancellationToken);
            db.Roles.RemoveRange(lamsRoles);
            await db.SaveChangesAsync(cancellationToken);
        }

        private static async Task MoveStaffAsync(
            CreditFlowDbContext db, string fromCode, string toCode, bool deactivate, CancellationToken cancellationToken)
        {
            var from = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Code == fromCode, cancellationToken);
            if (from is null)
            {
                return;
            }

            var to = (await db.Roles.AsNoTracking().SingleAsync(r => r.Code == toCode, cancellationToken)).ToProfile();
            var fromId = new RoleId(from.Id);
            var members = await db.StaffMembers.Where(s => s.RoleId == fromId).ToListAsync(cancellationToken);
            foreach (var member in members)
            {
                var moved = member.AssignRole(to, member.BranchId);
                if (moved.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"Moving staff member {member.Id} from {fromCode} to {toCode} failed: {moved.Error!.Description}");
                }

                if (deactivate)
                {
                    member.Deactivate();
                }
            }

            await db.SaveChangesAsync(cancellationToken);
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
        role.Permissions.AddRange(ValidatedPermissions(seed.Code, seed.Kind, permissions)
            .Select(p => new RolePermission { RoleId = role.Id, Permission = p }));
        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sets an existing role's fixed definition and replaces its permissions with the seed's matrix.</summary>
    public static async Task ReplacePermissionsAsync(CreditFlowDbContext db, RoleSeed seed, CancellationToken cancellationToken)
    {
        var role = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Code == seed.Code, cancellationToken);
        ApplyFixedDefinition(role, seed);

        var permissions = ValidatedPermissions(seed.Code, seed.Kind, seed.Permissions);
        role.Permissions.RemoveAll(p => !permissions.Contains(p.Permission));
        foreach (var permission in permissions.Where(p => role.Permissions.All(rp => rp.Permission != p)))
        {
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Grants permissions to an existing role (skips those it already holds).</summary>
    public static async Task GrantAsync(
        CreditFlowDbContext db, string roleCode, IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        var role = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Code == roleCode, cancellationToken);
        var requested = permissions.Distinct(StringComparer.Ordinal).ToList();
        ValidatedPermissions(roleCode, role.Kind, role.Permissions.Select(p => p.Permission).Concat(requested));

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

    /// <summary>The code-owned part of a role, re-applied on every start (never permissions or the MFA flag).</summary>
    public static void ApplyFixedDefinition(ApplicationRole role, RoleSeed seed)
    {
        role.DisplayName = seed.DisplayName;
        role.Description = seed.Description;
        role.Kind = seed.Kind;
        role.DataScope = seed.DataScope;
        role.Dashboard = seed.Dashboard;
        role.BranchRule = seed.BranchRule;
        role.IsSystem = true;
    }

    /// <summary>
    /// A seed unit must respect the same rules as the role editor: known permissions and segregation of duties.
    /// </summary>
    private static HashSet<string> ValidatedPermissions(string roleCode, RoleKind kind, IEnumerable<string> permissions)
    {
        var requested = permissions.ToHashSet(StringComparer.Ordinal);

        var unknown = requested.FirstOrDefault(p => !PermissionCatalog.Contains(p));
        if (unknown is not null)
        {
            throw new InvalidOperationException($"Seed grants unknown permission '{unknown}' to role {roleCode}.");
        }

        var set = PermissionCatalog.CreateSet(kind, requested);
        if (set.IsFailure)
        {
            throw new InvalidOperationException($"Seed grant to role {roleCode} is invalid: {set.Error!.Description}");
        }

        return requested;
    }
}
