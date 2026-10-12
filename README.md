# CreditFlow — retail loan origination

CreditFlow is the bank's internal system for granting loans (the bank's test task, `docs/CreditFlow-task.md`), built
as an ASP.NET Core 9 MVC monolith (Clean Architecture). It continues an earlier codebase (LAMS, Phase 1: sign-in with
MFA, permission-based authorization and the Admin area). The implementation plan and progress are in
[PLAN.md](PLAN.md).

## Decisions

1. **EF Core instead of Dapper — agreed with the bank in writing.** The CreditFlow task names Dapper; the bank
   confirmed that EF Core may be used instead. What the task asks of Dapper is met with EF Core: the Unit of Work is
   the scoped `DbContext` plus an explicit transaction for multi-step use cases (audit records and outbox messages
   are saved in the same transaction); optimistic concurrency uses SQL Server `rowversion`; raw SQL (locks, gapless
   numbering) goes only through interpolated, parameterised `FromSql` / `ExecuteSql`; reads are `AsNoTracking`
   projections without lazy loading. EF Core is referenced only by Infrastructure (architecture test).
2. **The roles come from the CreditFlow task; the role and permission mechanism was kept.** The seven roles of the
   task replaced the LAMS roles (seed unit `0004_creditflow_roles`). The mechanism is unchanged: permissions are stored
   per role in the database and edited by the Admin at runtime, code checks permissions (never role names), per-role
   behaviour that is not a permission is data on the role, and the cached access profile is invalidated explicitly.
3. **Role kinds enforce segregation of duties.** Every role is Business, Read-only or Administrative. A business role
   cannot hold `admin.*` permissions, the Admin role holds only `admin.*` permissions, and a read-only role (Auditor)
   holds only permissions the catalog marks read-only — enforced by the domain for seeds and for every edit in the role
   editor, so the Auditor cannot be given a write permission even by mistake.
4. **CQRS-lite: repositories in the Domain, query services in Application.** As the task requires, every aggregate
   root has one repository interface, declared in the Domain next to the aggregate (`IStaffRepository`,
   `IRoleRepository`, `IBranchRepository`, …) and implemented with EF Core in Infrastructure; repositories load and
   save aggregates only. Lists, inboxes and reports do not go through aggregates: they are query services declared in
   Application (`IRoleQueries`, `IBranchQueries`, `IUserDirectory`, `IAuditLogQueries`, …) that return read models
   from `AsNoTracking` projections. Architecture tests enforce both placements.

### Roles

| Task role | Code | Kind | Data scope | Permissions (seeded; editable) |
|---|---|---|---|---|
| Operator (branch) | `OP` | Business | own branch | clients (view, manage), applications (view, create, cancel, record client response), documents (view, upload), contracts, disburse, loans (view, record repayment), reports |
| Underwriter | `UW` | Business | whole bank | view clients / applications / documents / loans, decide up to the Underwriter threshold, counter-offer, reports |
| Senior Underwriter | `SUW` | Business | whole bank | Underwriter + decide up to the Senior threshold (incl. escalated files), override scoring rule, export reports |
| Credit Committee | `CC` | Business | whole bank | view clients / applications / documents / loans, committee vote, reports |
| Compliance | `COMP` | Business | whole bank | view clients / applications / documents, compliance review |
| Auditor | `AUD` | Read-only | whole bank | every read-only permission: clients, applications, documents, loans, reports + export, audit log |
| Admin | `SYSADMIN` | Administrative | none | every `admin.*` permission (users, roles, branches, audit log, products, scoring rules, approval thresholds, FX rates, holidays) |

The Admin role keeps the code `SYSADMIN` from Phase 1 (role codes are immutable); it is shown as "Admin".

## Running locally

**Requirements:** .NET SDK 9.0.x (see `global.json`) and Docker Desktop (for SQL Server and for the integration tests).

1. **Database.** Copy `.env.example` to `.env`, set `MSSQL_SA_PASSWORD` (upper, lower, digit and symbol, at least
   8 characters), then start SQL Server 2022:

   ```bash
   docker compose up -d
   ```

2. **Secrets** (user-secrets of `src/CreditFlow.Web`; nothing secret is committed):

   ```bash
   cd src/CreditFlow.Web
   dotnet user-secrets set "ConnectionStrings:CreditFlow" "Server=localhost,1433;Database=CreditFlow;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True"
   dotnet user-secrets set "Seed:Admin:Password" "<initial sysadmin password, min. 12 chars, upper/lower/digit/symbol>"
   dotnet user-secrets set "Seed:DevUsers:Password" "<password for all demo users, same rules>"
   ```

3. **Run:**

   ```bash
   dotnet run --project src/CreditFlow.Web --launch-profile https
   ```

   Open <https://localhost:7180>. In Development the database is created and migrated on start
   (`Database:ApplyMigrationsOnStartup`), and the reference data and accounts below are seeded.
   `/health` answers `Healthy` without signing in.

4. **Tests:** `dotnet test` (Docker must be running: the integration tests start their own SQL Server container).

## Accounts

There is no self-registration. Accounts are created by the Admin (Admin › Users), who receives a temporary password
(valid 72 hours, shown once) that the user must change at first sign-in.

**Admin** — seeded in every environment from configuration: user name `sysadmin` (`Seed:Admin:UserName`,
Development), password `Seed:Admin:Password`. It must change the password at first sign-in and lands on Admin › Users.
It has only `admin.*` permissions (segregation of duties) and sees no business data.

**Demo personas** — Development only (`Seed:DevUsers:Enabled`), all with the password `Seed:DevUsers:Password` and no
forced password change. Sign in with the user name or the email. There are enough people for four-eyes (one person
takes one decision per application).

| User name | Role | Branch |
|---|---|---|
| `anna.gelashvili` | Operator | Vake |
| `salome.janelidze` | Operator | Vake |
| `davit.maisuradze` | Operator | Batumi |
| `nika.beridze` | Underwriter | — |
| `giorgi.kapanadze` | Underwriter | — |
| `tamta.tatishvili` | Senior Underwriter | — |
| `mariam.lomidze` | Senior Underwriter | — |
| `irakli.tsereteli` | Credit Committee | — |
| `sophio.gogichaishvili` | Credit Committee | — |
| `zurab.kapanadze` | Credit Committee | — |
| `nino.kvaratskhelia` | Compliance | — |
| `levan.abashidze` | Auditor | — |

## Security behaviour worth knowing

- Sessions: 30 minutes sliding, at most 10 hours after sign-in (a password change does not extend it). Deactivation,
  forced sign-out, password reset and changes to role or branch end open sessions within one minute (security stamp
  validation). Permission changes apply on the next request.
- Lockout: 5 failed attempts lock the account for 15 minutes (the Admin can unlock it). As in ASP.NET Core Identity,
  a locked account shows the lockout page even when the correct password is entered; this reveals that the account
  exists, which is accepted for an internal staff system. Unknown user, wrong password and inactive account all show
  the same message; the audit log records the real reason.
- MFA (authenticator app) is required when the role or the user requires it; enrolment is then forced at sign-in.
- Every sign-in, security event and administrative change is in the audit log. It is append-only in code and in the
  database. The Admin and the Auditor can read it; links to administration pages are shown only to the Admin.
- Logs go to the console and to the `Logs` table (Serilog SQL Server sink; time, level, message, exception, user,
  request path, correlation id, other properties as JSON). The table is created by an EF Core migration, not by the
  sink. Settings: `DatabaseLog` (`Enabled`, `MinimumLevel`, `BatchPeriod`).
- **Masking.** One rule (`SensitiveData`) serves logs and audit records: any 11-digit number (a personal number) keeps
  only its last four digits (`*******1001`), fields named like a password, token, key or recovery code are hidden, and
  personal / document number fields keep their last four characters. In logs, a wrapping sink masks every event before
  any sink sees it — template text, property values at any depth and exception text (database errors can quote a
  duplicate personal number). In audit records, the before/after JSON is masked when it is written.
- The app is meant to run locally as a single instance. Behind a reverse proxy, forwarded headers would have to be
  configured first (audit IP addresses are the direct peer).

## Assumptions

1. MFA is required if the role **or** the user requires it; the Admin's "require two-factor" forces enrolment at the
   next sign-in.
2. The Admin role must keep `admin.users.manage` and `admin.roles.manage`; at least one active Admin must remain, and
   an Admin cannot deactivate or demote themselves.
3. Fonts and htmx are self-hosted, so the Content Security Policy allows only `'self'`.
4. Demo user names are `firstname.lastname`, emails `firstname.x@pcb.ge`.
5. Migrations run at start-up only when `Database:ApplyMigrationsOnStartup=true` (Development). Seeding runs on every
   start and is versioned (below).
6. English only; a Georgian (`ka`) culture is registered and falls back to English.

## Reference data and seeding

Reference data (roles, role permissions, branches) is seeded at start-up in every environment by
`ReferenceDataSeeder` (`src/CreditFlow.Infrastructure/Persistence/Seeding`). Seeding is **versioned**:

- Every change to reference data is a **seed unit**: a class implementing `ISeedUnit` with an ordered id
  `NNNN_snake_case_description` (for example `0002_initial_branches`).
- The units are listed, in order, in `SeedUnits.All`. Each pending unit runs **once**, in its own transaction, and is
  recorded in the `SeedHistory` table (`SeedId`, `AppliedAt`).
- A unit that has run never runs again. From then on the Admin owns the data: permissions removed in the role editor
  or deleted branches are **never** brought back by a restart.
- Units are idempotent (they skip what already exists), so a unit can safely run on a database where some of its data
  is already present.
- The **fixed definition** of each role is code-owned and re-applied on every start: display name, description, kind,
  data scope, dashboard and branch rule (from `RoleSeedCatalog`). Permissions and the role's "require MFA" flag are
  **not** re-applied: they belong to the Admin.

Current units:

| Seed id | What it does |
|---|---|
| `0001_initial_roles_permissions` | Retired (created the LAMS roles; replaced by `0004`). |
| `0002_initial_branches` | Creates the 6 branches from `BranchSeedCatalog`. |
| `0003_initial_authority_limits` | Retired (created the LAMS authority limits; the table was dropped). |
| `0004_creditflow_roles` | Creates the seven CreditFlow roles with the matrix from `RoleSeedCatalog`. On a Phase 1 database it also gives the kept roles (`UW`, `SYSADMIN`) the new matrix, moves Loan Officers to Operator, moves the users of the other LAMS roles to Auditor and deactivates them, and deletes the LAMS roles. |

Rules:

- **Never edit, rename, reorder or delete a unit that may have run anywhere.** Change data by appending a new unit.
  A unit may be **retired** (its id stays in the list, it does nothing) only when a later unit replaces everything it
  created, so that a fresh database and an upgraded one end in the same state.
- Use the idempotent helpers in `SeedOperations` (`CreateRoleIfMissingAsync`, `ReplacePermissionsAsync`,
  `GrantAsync`, `AddBranchIfMissingAsync`). They reject unknown permissions and grants that break segregation of
  duties for the role's kind.
- Code never checks role codes; role codes appear only in reference data, seed units and tests.

### Adding a new role

1. **Role code.** Add a constant to `RoleCodes` (`src/CreditFlow.Application/ReferenceData/RoleCodes.cs`)
   (2–16 uppercase letters or digits).
2. **New permissions (if any).** Add the constants to `Permissions` and their module, display name, description and
   read-only flag to `PermissionCatalog.All` (`src/CreditFlow.Application/Authorization`). Administration permissions
   start with `admin.`.
3. **Fixed definition.** Add a `RoleSeed` to `RoleSeedCatalog.All` with the display name, description, `Kind`,
   `DataScope`, `Dashboard`, branch rule and the role's initial `Permissions`. If the role needs a new dashboard, add a
   `DashboardKind` value and its resource texts.
4. **Seed unit.** Append a unit to `SeedUnits.All` with the next number that calls
   `SeedOperations.CreateRoleIfMissingAsync(db, seed, seed.Permissions, cancellationToken)`.
5. **Tests.** Add the role's column to the expected matrix in `PermissionMatrixTests`, update `ReferenceDataTests`,
   and add the new id to the expected list in `SeedHistoryTests`.
6. **Migration.** None is needed for data only. The next start of the application applies the unit once on every
   database.

### Granting a new permission to an existing role

1. Add the permission to `Permissions` / `PermissionCatalog` (if it is new).
2. Add it to the role's `Permissions` in `RoleSeedCatalog` (so the catalog keeps describing the default matrix and the
   matrix test stays accurate).
3. Append a unit, e.g. `0005_grant_uw_export`, that calls
   `SeedOperations.GrantAsync(db, RoleCodes.Underwriter, [Permissions.Reports.Export], cancellationToken)`.
4. Update the expected matrix in `PermissionMatrixTests` and the expected ids in `SeedHistoryTests`.

On databases where the Admin already removed that permission before the unit ran, the unit grants it once; afterwards
the Admin can remove it again and it stays removed.
