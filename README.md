# LAMS — Loan Applications Management System

Retail loan origination system for PCB. ASP.NET Core 9 MVC modular monolith (Clean Architecture).

Phase 1 delivers sign-in (with MFA), permission-based authorization and the Admin area (users, roles & permissions,
authority limits, branches, audit log). The implementation plan and progress are in [PLAN.md](PLAN.md).

## Running locally

**Requirements:** .NET SDK 9.0.x (see `global.json`) and Docker Desktop (for SQL Server and for the integration tests).

1. **Database.** Copy `.env.example` to `.env`, set `MSSQL_SA_PASSWORD` (upper, lower, digit and symbol, at least
   8 characters), then start SQL Server 2022:

   ```bash
   docker compose up -d
   ```

2. **Secrets**  (user-secrets of `src/Lams.Web`; nothing secret is committed):

   ```bash
   cd src/Lams.Web
   dotnet user-secrets set "ConnectionStrings:Lams" "Server=localhost,1433;Database=Lams;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True"
   dotnet user-secrets set "Seed:Admin:Password" "<initial sysadmin password, min. 12 chars, upper/lower/digit/symbol>"
   dotnet user-secrets set "Seed:DevUsers:Password" "<password for all demo users, same rules>"
   ```

3. **Run:**

   ```bash
   dotnet run --project src/Lams.Web --launch-profile https
   ```

   Open <https://localhost:7180>. In Development the database is created and migrated on start
   (`Database:ApplyMigrationsOnStartup`), and the reference data and accounts below are seeded.
   `/health` answers `Healthy` without signing in.

4. **Tests:** `dotnet test` (Docker must be running: the integration tests start their own SQL Server container).

## Accounts

There is no self-registration.Accounts are created by an administrator (Admin › Users), who receives a temporary
password (valid 72 hours, shown once) that the user must change at first sign-in.

**System administrator** — seeded in every environment from configuration: user name `sysadmin`
(`Seed:Admin:UserName`, Development), password `Seed:Admin:Password`. It must change the password at first sign-in and
lands on Admin › Users. It has only `admin.*` permissions (segregation of duties) and sees no business data.

**Demo personas** — Development only (`Seed:DevUsers:Enabled`), all with the password `Seed:DevUsers:Password` and no
forced password change. Sign in with the user name or the email.

| User name | Role | Branch | Level / segment |
|---|---|---|---|
| `anna.gelashvili` | Loan Officer (LO) | Batumi | Junior |
| `salome.janelidze` | Loan Officer (LO) | Saburtalo | Standard |
| `davit.maisuradze` | Loan Officer (LO) | Tbilisi HQ | Senior |
| `nika.beridze` | Underwriter (UW) | Saburtalo | Junior (₾15,000) |
| `giorgi.kapanadze` | Underwriter (UW) | Tbilisi HQ | Standard (₾30,000) |
| `tamta.tatishvili` | Underwriter (UW) | Tbilisi HQ | Senior (₾50,000) |
| `mariam.lomidze` | Branch Manager (BM) | Tbilisi HQ | — (₾150,000) |
| `lasha.chkheidze` | Business Development Manager (BDM) | — | — |
| `keti.nadiradze` | Back Office (BO) | — | — |
| `nino.kvaratskhelia` | AML / KYC Analyst (AML) | — | — |
| `levan.abashidze` | Financial Analysis & Budgeting (FAB) | — | — |
| `irakli.tsereteli` | Head (HD) | — | Business |
| `sophio.gogichaishvili` | Head (HD) | — | Retail |
| `zurab.kapanadze` | Bank Director (DIR) | — | — |

The amounts are the seeded credit decision limits (GEL); they are editable in Admin › Authority limits.

**A quick check of the definition of done:** sign in as `sysadmin`, change the password, create a Senior Underwriter in
Vake (Admin › Users › New user), sign in as that user in another browser and change the password: the sidebar shows
Dashboard, Applications, Packages and Reports only, the dashboard shows "Authority limit: ₾50,000", and `/Admin/Users`
shows Access denied. Then remove *View reports* from the Underwriter role (Admin › Roles & permissions): Reports
disappears for that user on the next page load, without signing out.

## Security behaviour worth knowing

- Sessions: 30 minutes sliding, at most 10 hours after sign-in (a password change does not extend it). Deactivation,
  forced sign-out, password reset and changes to role, branch, segment or authority level end open sessions within
  one minute (security stamp validation). Permission changes apply on the next request.
- Lockout: 5 failed attempts lock the account for 15 minutes (an administrator can unlock it). As in ASP.NET Core
  Identity, a locked account shows the lockout page even when the correct password is entered; this reveals that the
  account exists, which is accepted for an internal staff system. Unknown user, wrong password and inactive account all
  show the same message; the audit log records the real reason.
- MFA (authenticator app) is required when the role or the user requires it; enrolment is then forced at sign-in.
- Every sign-in, security event and administrative change is in the audit log (Admin › Audit log). It is append-only
  in code and in the database.
- The app is meant to run locally as a single instance. Behind a reverse proxy, forwarded headers would have to be
  configured first (audit IP addresses are the direct peer).

## Open questions

Seeded as instructed; all are editable in Admin › Roles & permissions or Admin › Authority limits.

1. **BDM documents.** BDM keeps `documents.view`, upload and delete-own-new (FRD §6.4) although FRD §5.3 calls the
   role read-only.
2. **Back office scope.** BO has data scope Organization; filtering its queue is deferred to Phase 2.
3. **Reassign.** `applications.reassign` is defined but granted to no role.
4. **Actions menu.** `applications.mark-priority`, `tag`, `add-internal-note`, `repull-credit-data`,
   `reverify-income`, `print` and `archive` are defined but granted to no role.
5. **Credit Committee and CRO** (Flow C) are not created as roles. "Refer to committee" exists as a permission; the
   committee itself is for a later phase (README "Adding a new role" describes how).

## Assumptions

1. Credit decision limits are seeded in GEL only. When no limit row matches (e.g. a USD loan), the decision is
   **escalated**; "not authorized" applies only to users without `applications.decide`.
2. Head pricing exceptions: the rate gap must be **below** 2.0 pp (strict) and the customer ROAE **at least** 15 %.
   The Director has no limit. A Head approves only their own segment.
3. MFA is required if the role **or** the user requires it; the administrator's "require two-factor" forces enrolment
   at the next sign-in.
4. The System Administrator role must keep `admin.users.manage` and `admin.roles.manage`; at least one active
   administrator must remain, and administrators cannot deactivate or demote themselves.
5. Fonts and htmx are self-hosted, so the Content Security Policy allows only `'self'`.
6. Demo user names are `firstname.lastname`, emails follow the prototype (`mariam.l@pcb.ge`).
7. Migrations run at start-up only when `Database:ApplyMigrationsOnStartup=true` (Development). Seeding runs on every
   start and is versioned (below).
8. English only; a Georgian (`ka`) culture is registered and falls back to English.

## Reference data and seeding

Reference data (roles, role permissions, branches, authority limits) is seeded at start-up in every environment by
`ReferenceDataSeeder` (`src/Lams.Infrastructure/Persistence/Seeding`). Seeding is **versioned**:

- Every change to reference data is a **seed unit**: a class implementing `ISeedUnit` with an ordered id
  `NNNN_snake_case_description` (for example `0001_initial_roles_permissions`).
- The units are listed, in order, in `SeedUnits.All`. Each pending unit runs **once**, in its own transaction, and is
  recorded in the `SeedHistory` table (`SeedId`, `AppliedAt`).
- A unit that has run never runs again. From then on the administrator owns the data: permissions removed in the role
  editor, deleted limits or deleted branches are **never** brought back by a restart.
- Units are idempotent (they skip what already exists), so a unit can safely run on a database where some of its data
  is already present.
- The **fixed definition** of each role is code-owned and re-applied on every start: display name, description,
  data scope, dashboard, branch/segment/authority-level rules and the administrative flag (from `RoleSeedCatalog`).
  Permissions and the role's "require MFA" flag are **not** re-applied: they belong to the administrator.

Current units:

| Seed id | What it does |
|---|---|
| `0001_initial_roles_permissions` | Creates the 10 initial roles (`SeedUnits.InitialRoleCodes`) with the permission matrix from `RoleSeedCatalog`. |
| `0002_initial_branches` | Creates the 6 branches from `BranchSeedCatalog`. |
| `0003_initial_authority_limits` | Creates the credit-decision and pricing-exception limits from `AuthorityLimitSeedCatalog`. |

Rules:

- **Never edit, rename, reorder or delete a unit that may have run anywhere.** Change data by appending a new unit.
- Use the idempotent helpers in `SeedOperations` (`CreateRoleIfMissingAsync`, `GrantAsync`,
  `AddBranchIfMissingAsync`, `AddAuthorityLimitIfMissingAsync`). `GrantAsync` rejects unknown permissions and grants
  that break segregation of duties (an administrative role may hold only `admin.*`, a business role never).
- Code never checks role codes; role codes appear only in reference data, seed units and tests.

### Adding a new role (e.g. in Phase 2)

1. **Role code.** Add a constant to `RoleCodes` (`src/Lams.Application/ReferenceData/RoleCodes.cs`), e.g.
   `public const string CreditCommittee = "CC";` (2–16 uppercase letters or digits).
2. **New permissions (if any).** Add the constants to `Permissions` and their module, display name and description to
   `PermissionCatalog.All` (`src/Lams.Application/Authorization`). Administration permissions must start with `admin.`.
3. **Fixed definition.** Add a `RoleSeed` to `RoleSeedCatalog.All` with the display name, description, `DataScope`,
   `Dashboard`, branch/segment/authority-level rules, `IsAdministrative`, and the role's initial `Permissions`.
   Do **not** add the code to `SeedUnits.InitialRoleCodes`: unit `0001` stays as it was.
   If the role needs a new dashboard, add a `DashboardKind` value and its dashboard page.
4. **Seed unit.** Append a unit to `SeedUnits.All` with the next number, which creates the role and grants its
   permissions explicitly:

   ```csharp
   private sealed class AddCreditCommitteeRole : ISeedUnit
   {
       public string Id => "0004_add_role_credit_committee";

       public async Task ApplyAsync(LamsDbContext db, CancellationToken cancellationToken)
       {
           var seed = RoleSeedCatalog.Get(RoleCodes.CreditCommittee);
           await SeedOperations.CreateRoleIfMissingAsync(db, seed, seed.Permissions, cancellationToken);
       }
   }
   ```

5. **Authority limits (if the role has any).** Add them in the same unit (or a following one) with
   `SeedOperations.AddAuthorityLimitIfMissingAsync(db, new AuthorityLimitSeed(...), cancellationToken)`.
   Do not add rows to `AuthorityLimitSeedCatalog` (unit `0003` stays as it was).
6. **Tests.** Add the role's column to the expected matrix in `PermissionMatrixTests`, update the role counts in
   `ReferenceDataTests`, and add the new id to the expected list in `SeedHistoryTests`.
7. **Migration.** None is needed for data only. Run `dotnet test`: the next start of the application applies the
   unit once on every database.

### Granting a new permission to an existing role

1. Add the permission to `Permissions` / `PermissionCatalog` (if it is new).
2. Add it to the role's `Permissions` in `RoleSeedCatalog` (so the catalog keeps describing the default matrix and the
   matrix test stays accurate).
3. Append a unit, e.g. `0005_grant_uw_reassign`, that calls
   `SeedOperations.GrantAsync(db, RoleCodes.Underwriter, [Permissions.Applications.Reassign], cancellationToken)`.
4. Update the expected matrix in `PermissionMatrixTests` and the expected ids in `SeedHistoryTests`.

On databases where an administrator already removed that permission before the unit ran, the unit grants it once;
afterwards the administrator can remove it again and it stays removed.
