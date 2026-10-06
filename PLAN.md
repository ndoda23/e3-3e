# LAMS Phase 1 — Auth, permission-based authorization, admin area

## Context
The repo contains only `docs/`. Phase 1 delivers a running ASP.NET Core 9 MVC modular monolith with:
- real Identity login;
- permission-based authorization, with permissions stored in the DB per role and resolved per request through HybridCache;
- an Admin area for users, roles, authority limits, branches and the audit log;
- the authorization foundation (data scope and authority limits) that Phase 2 (Application Intake) can use as-is.

I read FRD §5.1–5.3, §6.4, Flows A–D, §8.2 and §10, plus the whole `docs/lams-ui-reference/` folder (CSS tokens, layout, team roster, icons and both screenshots).

The prompt was pasted twice. I follow the **second** package rule: pin Microsoft.AspNetCore.*, Microsoft.EntityFrameworkCore.* and Microsoft.Extensions.* to 9.x. Other packages use their latest stable version that supports net9.0.

---

## 0. Progress (as of 2026-10-06, end of day)

**Phase 1 is complete** (Steps 0–8). Step 8 was kept light by agreement: the app runs only locally, single instance. Step 7 is done in five sub-steps (7a–7e), each with its own commit.
Sign-in works since Step 6 (accounts: see README in Step 8; for now the seeded `sysadmin` and, in Development, the demo personas).

| Step | Status | Commit(s) |
|---|---|---|
| 0 — Plan + repository | done | `8207982` |
| 1 — Scaffold | done | `7e43bd7` |
| 2 — Domain | done | `7a79fea`; revised (StaffMember split) `2100f9e` |
| — Line endings normalized to LF | done | `768107d` |
| 3 — Application | done | `c9532f5` |
| 4 — Infrastructure | done | `0ae95f6`; seed versioning `561fe1e` |
| 4b — View inventory | approved by the user, as listed below (no code) | — |
| 5 — Web shell | done | `896a243` |
| 6 — Account | done | `a0d3f0f` (middleware internal), `f1bb742` |
| 7a — Admin › Users (+ 3 UI fixes) | done | `0af30fc` |
| 7b — Admin › Roles & permissions | done | `d59e3d7` |
| 7c — Admin › Authority limits | done | `2c98a18` |
| 7d — Admin › Branches | done | `68148fc` |
| 7e — Admin › Audit log | done | `319312b` |
| 8 — Integration tests and docs (light) | done | (this commit) |

Test status after Step 8: build with 0 warnings; 798 tests green (Domain 135, Application 519, Architecture 9, Integration 135 — Testcontainers SQL Server).

### Seed versioning (SeedHistory, seed units)
**Fully implemented and tested.** `SeedHistory` table (migration `AddSeedHistory`); units `0001_initial_roles_permissions`, `0002_initial_branches`, `0003_initial_authority_limits` run once each, in their own transaction; fixed role definitions re-applied on every start; `SeedOperations` helpers (`GrantAsync` enforces known permissions and segregation of duties); README documents adding a role / a grant.
Tests (7): every unit recorded once; deleted limits of a type do not come back; deleted branch and revoked permissions do not come back; fixed definitions restored while admin-owned fields survive; grant idempotency and SoD; unit-id rules; initial role list.
Nothing remains. Known limitation (not a requirement): if two app instances start at the same moment on an empty database, both may try to apply the same unit; the `SeedHistory` primary key makes the second one fail at start-up (it can simply be restarted). An application lock (`sp_getapplock`) can be added if multi-instance deployment is planned.

### Pending notes (to do)
1. **`SecurityHeadersMiddleware` back to `internal`** — **done** at the start of Step 6: `internal` again, tests reach it through `<InternalsVisibleTo Include="Lams.IntegrationTests" />` in `Lams.Web.csproj`.
2. **Controller architecture test must find at least one controller** — **done** in `896a243` (`Controllers_do_not_use_DbContext_directly` asserts ≥ 1 non-abstract controller before applying the rule). Keep it that way.
3. **Lockout clock (Step 8)** — **answered** in Step 6: ASP.NET Core Identity 9 (`UserManager`, `SignInManager`) has no `TimeProvider`; lockout uses the system clock. The Step 8 "login works again after 15 minutes" test will therefore set `LockoutEnd` in the past directly (and say so in the test) instead of advancing `FakeTimeProvider`.

### Decisions confirmed by the user (2026-10-06)
All 12 decisions below were reviewed and confirmed. No README "Known limitations" section: the app runs only locally (single instance), so the concurrent start-up note above needs no fix or documentation.
1. **Serilog.AspNetCore 9.0.0** instead of 10.0.0 (10.x pulls Microsoft.Extensions.* 10.0 into net9.0).
2. **Manual foreign keys + SQL view.** EF Core cannot map FKs between strongly typed ids and Identity's Guid keys; three FKs are created by hand in the migration (enforced and tested), and `vw_UserDirectory` serves user queries.
3. **Access cache uses two keys** (`access:user:{id}`, `access:role:{id}`) instead of HybridCache tags (the role is not known before loading).
4. **Attribute rules:** branch is *optional* for BDM/BO/AML/FAB/HD/DIR; segment and authority level are *not allowed* outside HD and LO/UW (values are cleared).
5. **Branch code is immutable** after creation (name and city editable).
6. **Authority-limit rows follow the role's level rule** (BM row has no level, UW rows must have one); limits only for business roles.
7. **Demo personas do not have to change their password**; the seeded SYSADMIN does.
8. **Changing a user's user name ends their sessions** (Identity rotates the security stamp on `SetUserNameAsync`).
9. **UI:** prototype search box and notification bell left out (non-functional in Phase 1); htmx history cache disabled (no page snapshots in browser storage).
10. **Localization:** only an explicit choice (culture cookie) switches language; the browser's Accept-Language is ignored ("English first"). Georgian resource file is empty; fonts are the Latin subset (Georgian uses the system font).
11. **`/health` is anonymous** and returns only "Healthy"/"Unhealthy".
12. **Reverse proxy:** forwarded headers are not configured, so audit IP addresses are the direct peer. Must be configured (`UseForwardedHeaders` with known proxies) before deploying behind a proxy/load balancer.

## 1. Solution structure

```
LAMS/
├─ Lams.sln
├─ global.json                    (SDK 9.0.x, rollForward latestFeature)
├─ Directory.Build.props          (net9.0, nullable, implicit usings, LangVersion latest,
│                                  TreatWarningsAsErrors, AnalysisLevel latest-recommended)
├─ Directory.Packages.props       (Central Package Management)
├─ .editorconfig  .gitignore  .config/dotnet-tools.json (dotnet-ef 9.x)
├─ docker-compose.yml             (SQL Server 2022 for local dev; SA password from gitignored .env)
├─ README.md                      (how to run)
├─ src/
│  ├─ Lams.Domain/
│  │  ├─ Common/          Result, Error, IdGenerator, IAuditedEntity (CreatedAt/By, UpdatedAt/By stamps)
│  │  ├─ Roles/           RoleId, RoleCode, PermissionCode, DataScope, DashboardKind, RoleProfile,
│  │  │                   AttributeRule, PermissionSet (SoD rule)
│  │  ├─ Users/           UserId, StaffMember (business user: single RoleId + attribute rules),
│  │  │                   StaffAccess (access snapshot), AuthorityLevel, Segment,
│  │  │                   AdminContinuityPolicy (domain service), TemporaryPasswordPolicy, SessionLifetimePolicy
│  │  ├─ Branches/        BranchId, Branch
│  │  ├─ AuthorityLimits/ AuthorityLimitId, AuthorityLimit, LimitType, Money/Currency,
│  │  │                   CreditDecisionOutcome, PricingExceptionOutcome
│  │  └─ Auditing/        AuditLogId, AuditLog (append-only)
│  ├─ Lams.Application/
│  │  ├─ Abstractions/    ICurrentUser, IIdentityService (port over UserManager: accounts, passwords, MFA,
│  │  │                   lockout, security stamp), IStaffRepository, other feature repositories,
│  │  │                   IUnitOfWork (one transaction), IAuditWriter
│  │  ├─ Authorization/   Permissions catalog (+ PermissionDefinition: code, module, name, description),
│  │  │                   RoleSeedCatalog (the 10 roles + the matrix), IUserAccessProfileService,
│  │  │                   UserAccessProfile, IAccessCacheInvalidator, IScopedResource, IPricedResource,
│  │  │                   IDataScopeEvaluator, IAuthorityLimitEvaluator
│  │  ├─ Users/  Roles/  Branches/  AuthorityLimits/  Auditing/  Account/
│  │  │                   (commands/queries as records + validators + an application service per feature,
│  │  │                    explicit mapping to DTO records)
│  │  └─ DependencyInjection.cs
│  ├─ Lams.Infrastructure/
│  │  ├─ Persistence/     LamsDbContext (IdentityDbContext<ApplicationUser, ApplicationRole, Guid>),
│  │  │                   Configurations/, Migrations/, Interceptors/AuditSaveChangesInterceptor,
│  │  │                   Seeding/ (ReferenceDataSeeder, AdminSeeder, DevUserSeeder)
│  │  ├─ Identity/        ApplicationUser (authentication data only), ApplicationRole, RolePermission,
│  │  │                   UserPasswordHistory, UserRoleSynchronizer (AspNetUserRoles ← StaffMember.RoleId),
│  │  │                   IdentityService, LamsSignInManager, LamsUserClaimsPrincipalFactory,
│  │  │                   PasswordHistoryValidator, TemporaryPasswordGenerator
│  │  ├─ Authorization/   UserAccessProfileService (HybridCache), AccessCacheInvalidator
│  │  ├─ Auditing/        AuditWriter (security events)
│  │  ├─ Options/         DatabaseOptions, SeedOptions, SecurityOptions (+ validators, ValidateOnStart)
│  │  └─ DependencyInjection.cs
│  └─ Lams.Web/
│     ├─ Program.cs  (composition root)
│     ├─ Authorization/   HasPermissionAttribute, PermissionPolicyProvider, PermissionRequirement/Handler,
│     │                   DataScopeRequirement/Handler, AuthorityLimit requirements/handlers (skeletons),
│     │                   PermissionTagHelper (asp-permission / asp-any-permission)
│     ├─ Authentication/  LamsCookieEvents (stamp validation → absolute lifetime; htmx-aware redirects)
│     ├─ Infrastructure/  GlobalExceptionHandler (IExceptionHandler + ProblemDetails), SecurityHeadersMiddleware,
│     │                   ForcedActionsFilter (must-change-password / MFA enrolment), HtmxExtensions
│     ├─ Controllers/     AccountController, ManageController, DashboardController, placeholder controllers
│     │                   (Applications, Packages, Reports), ErrorController
│     ├─ Areas/Admin/     Controllers + Views: Users, Roles, AuthorityLimits, Branches, AuditLog,
│     │                   placeholders: CreditPolicy, Pricing, Settings
│     ├─ ViewComponents/  Sidebar, UserCard, Topbar
│     ├─ Views/           (see the view inventory in Step 4b)
│     ├─ Resources/       SharedResource.en.resx (+ SharedResource.ka.resx stub), view/model resources
│     └─ wwwroot/         css/site.css (ported tokens), js/htmx.min.js (vendored 2.x), js/site.js,
│                         fonts/ (self-hosted Inter + JetBrains Mono woff2)
└─ tests/
   ├─ Lams.Domain.UnitTests/
   ├─ Lams.Application.UnitTests/      (permission matrix per role, evaluators with fake resources, validators)
   ├─ Lams.IntegrationTests/           (Testcontainers MsSql + WebApplicationFactory + FakeTimeProvider;
   │                                     also policy provider/handler/tag helper tests)
   └─ Lams.ArchitectureTests/          (NetArchTest)
```

**Key architectural decision: the user model is split in two (revised before Step 3).**
- **`StaffMember` (Domain/Users)** is the business user: full name, the single `RoleId`, branch, segment, authority level, active/available, capacity and the created/updated stamps. The attribute rules are its methods (`Create`, `AssignRole`, `ChangeBranch`, `ChangeSegment`, `ChangeAuthorityLevel`, `Activate`, `Deactivate`, `SetAvailability`, `ChangeCapacity`, `Rename`), all returning `Result`. Phase 2 entities reference staff by `UserId`.
- **`ApplicationUser : IdentityUser<Guid>` (Infrastructure/Identity)** holds only authentication data: user name, email, password hash, MFA (incl. the per-user RequireMfa flag), lockout, security stamp, MustChangePassword, TemporaryPasswordExpiresAt, LastLoginAt.
- **They share the same id (1:1, required)**, live in the same DbContext and are always saved in one transaction.
- **One role per user** is enforced by the single column `StaffMember.RoleId`. `AspNetUserRoles` is kept in sync with it inside the same transaction (Identity compatibility only; nothing reads roles from it). A unique index on `AspNetUserRoles.UserId` is a second safety net.
- `ApplicationRole : IdentityRole<Guid>` also lives in Infrastructure. Domain sees roles as `RoleProfile` and `PermissionSet`.
- `AdminContinuityPolicy` stays a domain service: Application checks it before calling `AssignRole` / `Deactivate`.
- Architecture tests enforce the split: no Identity user type outside Infrastructure, and the Identity account declares none of the business attributes (enabled in Step 4).

**No role-name checks in code.** Per-role behaviour that is not a permission is stored as **data on the role** and seeded:
- `DataScope`
- `DashboardKind`: Officer / Underwriter / Branch / BDM / BackOffice / AmlScreening / Fab / Exceptions / Admin
- `BranchRule`, `SegmentRule`, `AuthorityLevelRule`: Required / NotAllowed
- `IsAdministrative`: the SoD class
- `RequireMfa`

## 2. Entities

| Entity | Layer | Key fields |
|---|---|---|
| StaffMember | Domain | Id (UserId, = ApplicationUser.Id), FullName, RoleId (single value: **one role per user**), BranchId?, Segment?, AuthorityLevel?, IsActive, IsAvailable, Capacity, CreatedAt/By, UpdatedAt/By. Never deleted, only deactivated |
| ApplicationUser | Infra (Identity) | Authentication data only: UserName, Email, PasswordHash, TwoFactorEnabled + authenticator key / recovery codes, RequireMfa (per-user override), lockout fields, SecurityStamp, MustChangePassword, TemporaryPasswordExpiresAt?, LastLoginAt. Required 1:1 with StaffMember (shared primary key). AspNetUserRoles mirrors StaffMember.RoleId (unique index on UserId) |
| ApplicationRole | Infra (Identity) | Code (unique, immutable), DisplayName, Description, DataScope, DashboardKind, BranchRule, SegmentRule, AuthorityLevelRule, IsAdministrative, RequireMfa, IsSystem |
| RolePermission | Infra | (RoleId, Permission) composite PK |
| UserPasswordHistory | Infra | Id, UserId, PasswordHash, CreatedAt. The last 5 are kept |
| Branch | Domain | BranchId, Code, Name, City, IsActive |
| AuthorityLimit | Domain | AuthorityLimitId, RoleId, AuthorityLevel?, LimitType, Currency?, MaxAmount?, MaxRateGapPp?, MinCustomerRoaePct?. Unique on (RoleId, AuthorityLevel, LimitType, Currency) |
| AuditLog | Domain | AuditLogId (long), OccurredAt, UserId?, UserName, Action, EntityType, EntityId, BeforeJson, AfterJson, IpAddress, UserAgent, CorrelationId |
| DataProtectionKey | Infra | EF Core Data Protection key store |
| Value objects / enums | Domain | RoleId, UserId, BranchId, AuthorityLimitId, AuditLogId, DataScope {None, Own, Assigned, Branch, Segment, Organization}, AuthorityLevel {Junior, Standard, Senior}, Segment {Retail, Business}, LimitType {CreditDecision, PricingException}, Currency {GEL, USD, EUR} |

**Seeded roles:**

| Code | DataScope | Dashboard | Branch | Level | Segment |
|---|---|---|---|---|---|
| LO | Own | Officer | Req | Req | — |
| UW | Assigned | Underwriter | Req | Req | — |
| BM | Branch | Branch | Req | — | — |
| BDM | Organization | BDM | — | — | — |
| BO | Organization | BackOffice | — | — | — |
| AML | Organization | AmlScreening | — | — | — |
| FAB | Organization | Fab | — | — | — |
| HD | Segment | Exceptions | — | — | Req |
| DIR | Organization | Exceptions | — | — | — |
| SYSADMIN | None | Admin | NotAllowed | NotAllowed | NotAllowed |

**Seeded authority limits:**
- CreditDecision (GEL): UW Junior 15,000; UW Standard 30,000; UW Senior 50,000; BM 150,000.
- PricingException: HD with MaxRateGapPp 2.0 (exclusive: the gap must be < 2.0) and MinCustomerRoaePct 15 (inclusive). DIR with all values null, which means no limit.

**Permissions:** exactly the catalog and matrix from the prompt, plus the admin.* permissions.

These permissions are defined but granted to no role (open questions 3 and 4):
- `applications.reassign`
- `applications.mark-priority`
- `applications.tag`
- `applications.add-internal-note`
- `applications.repull-credit-data`
- `applications.reverify-income`
- `applications.print`
- `applications.archive`

## 3. Authorization design (summary)

**Permission checks**
- `[HasPermission(...)]` maps to the policy name `perm:<code>`.
- `PermissionPolicyProvider` builds a `PermissionRequirement` on demand.
- `PermissionHandler` checks the requirement against the user's cached `UserAccessProfile`.
- The Admin area has an area-wide "any admin.* permission" policy, plus a permission on each action.

**Cached profile (`UserAccessProfile`)**
- Built from **`StaffMember` + the role's profile and permissions** (never from `ApplicationUser` or the cookie).
- Contains UserId, RoleId, IsAdministrative, DataScope, Dashboard, BranchId, Segment, AuthorityLevel, IsActive and Permissions.
- Stored in HybridCache as two entries: `access:user:{id}` (staff snapshot: role id, branch, segment, level, active) and `access:role:{roleId}` (role definition + permissions). A role change removes one role entry for all its users; a user change removes the user entry. Expiry of 1 minute is only a safety net; explicit invalidation does the real work.
- Permission, data-scope and authority-limit checks read **only** this profile, never cookie claims.

**Resource-based checks (skeleton for Phase 2)**
- `IDataScopeEvaluator` checks Own / Assigned / Branch / Segment / Organization / None against an `IScopedResource` (OwnerUserId, AssignedUserId, BranchId, Segment).
- `IAuthorityLimitEvaluator.EvaluateCreditDecision` returns Within, Escalate or NotAuthorized:
  - NotAuthorized only when the user lacks `applications.decide`.
  - Escalate when the amount is over the limit **or when no limit row matches**.
- `EvaluatePricingException(IPricedResource{RateGapPp, CustomerRoaePct, Segment})` returns Within, RouteToDirector or NotAuthorized. A segment mismatch returns NotAuthorized.
- Thin ASP.NET `AuthorizationHandler<TReq, TResource>` wrappers live in Web.

**Tag Helper:** `asp-permission` / `asp-any-permission` hide the element when the user lacks the permission.

**Sidebar:**

| Item | Shown when |
|---|---|
| Dashboard | role DashboardKind ≠ Admin |
| Applications | applications.view-full OR aml.view-screening OR pricing.view-exceptions |
| Packages | applications.view-full |
| Reports | reports.view |
| Create Application (topbar) | applications.create |
| Admin → Users | admin.users.view |
| Admin → Roles | admin.roles.manage |
| Admin → Authority limits | admin.authority-limits.manage |
| Admin → Branches | admin.branches.manage |
| Admin → Audit log | admin.audit.view |
| Admin → Credit policy / Risk-based pricing / Settings (placeholders) | admin.credit-policy.manage / admin.pricing.manage / admin.settings.manage |

## 4. Implementation steps
Each step ends with a green build and tests, a summary and a suggested commit message.

**Progress tracking.**
- Each step heading carries a status: `[ ]` not started, `[x]` done.
- After each step I mark it `[x]` in PLAN.md and include that change in the step's commit.
- A new session reads PLAN.md first and continues from the first `[ ]` step.

### Step 0 — [x] Save the plan and set up the repository
- The folder is not a repository of its own: the enclosing git repository is the user profile (`C:\Users\Dell XPS`), shown by the `../../../../` paths in `git status`. Committing there would be wrong.
- Run `git init -b main` in `LAMS/` so the project gets its own repository. The outer repository ignores nested `.git` folders.
- Add the .NET `.gitignore`.
- Save this plan as `PLAN.md` in the repository root.
- Commit with the message `docs: add Phase 1 implementation plan`. The commit includes `docs/`, unchanged.

### Step 1 — [x] Scaffold
- Solution with 4 src and 4 test projects.
- Directory.Build.props, Directory.Packages.props, .gitignore, .editorconfig, global.json, dotnet-ef tool manifest, docker-compose.
- NetArchTest rules:
  - Domain depends on nothing.
  - Application does not reference Infrastructure or Web.
  - Controllers do not use DbContext.

### Step 2 — [x] Domain
- Strongly typed IDs, Result/Error, enums.
- Branch; AuthorityLimit with both evaluation kinds (no matching row → Escalate).
- RoleProfile + PermissionSet (SoD rule).
- **Revised before Step 3:** `StaffMember` entity with the attribute rules as methods (`Create`, `AssignRole`, `ChangeBranch`, `ChangeSegment`, `ChangeAuthorityLevel`, `Activate`, `Deactivate`, `SetAvailability`, `ChangeCapacity`, `Rename`), returning `Result`. Choosing SYSADMIN clears branch, segment and level. One role per user: a single `RoleId`. `StaffAccess` snapshot for change detection. `IAuditedEntity` for created/updated stamps. (Replaces the earlier `UserAssignment` and `UserRoleRule`.)
- AdminContinuityPolicy (domain service): at least one active SYSADMIN; no self-deactivation or self-demotion.
- `TemporaryPasswordPolicy.IsExpired(expiresAt, now)`.
- `SessionLifetimePolicy.IsExpired(issuedAt?, now, lifetime)`: a missing issuedAt counts as expired (fail closed).
- AuditLog.
- Domain unit tests for all of the above.

### Step 3 — [x] Application
- Permission catalog with metadata, and RoleSeedCatalog.
- `IUserAccessProfileService`, `UserAccessProfile` (built from `StaffMember` + role profile + permissions), `IAccessCacheInvalidator`.
- Data scope and authority limit evaluators.
- Feature services, commands/queries, FluentValidation validators, DTO records.
- Ports for the split user model: `IStaffRepository` (StaffMember), `IIdentityService` (the authentication account: create with a temporary password, reset password, lock/unlock, MFA flags, security stamp), `IUnitOfWork`. Creating a user creates the StaffMember and the account with the same id **in one transaction**.
- Every user-edit path goes through a single access-change step: it compares `StaffMember.Access` before and after. If Role, IsActive, BranchId, Segment or AuthorityLevel changed, it invalidates `access:user:{id}` **and** updates the security stamp, in the same transaction as the edit.
- Unit tests:
  - Every matrix row verified per role.
  - Evaluators with fake resources.
  - Validators.
  - The access-change step invalidates the cache and updates the stamp on every one of the five attributes, and does nothing when only non-access fields (e.g. FullName, Capacity) change.
- Implementation notes (as built):
  - `Error.ValidationFailed(details)` carries all field errors (`Error.Details`, each with `Target` = field name) so forms show every error at once.
  - `IUnitOfWork.ExecuteInTransactionAsync` commits only when the returned `Result` is a success. `StaffAccessChangeStep` rotates the stamp inside the transaction and invalidates the cache **after** commit (never on rollback).
  - `SecurityOptions` (session lifetimes, temporary password lifetime, stamp interval) lives in Application; the host binds and validates it.
  - Reference data (`RoleSeedCatalog`, `BranchSeedCatalog`, `AuthorityLimitSeedCatalog`, `RoleCodes`) lives in `Application/ReferenceData`. `RoleCodes` are constants for seeding/tests only; they are inlined by the compiler, so no architecture test can detect their use.
  - `RoleRules.RequiredAdministrativePermissions` = `admin.users.manage`, `admin.roles.manage` (locked in the editor).
  - The permission matrix test uses an independently written expected table (390 cells); a mutation check confirmed it fails when a seed grant is wrong.

### Step 4 — [x] Infrastructure
**Database**
- DbContext, configurations and value converters.
- **User model mapping:**
  - `StaffMembers` table: primary key = `ApplicationUser.Id`, a required 1:1 relationship to `AspNetUsers` (FK on the shared key, cascade off), FK `RoleId` → `AspNetRoles`, FK `BranchId` → `Branches`.
  - Account and staff member are added and saved together through one `SaveChangesAsync` / transaction (`IUnitOfWork`).
  - `UserRoleSynchronizer` runs inside the same save: whenever `StaffMember.RoleId` is added or changed, `AspNetUserRoles` is rewritten to exactly that one row. A unique index on `AspNetUserRoles.UserId` guards against drift.
  - `IAuditedEntity` stamps (CreatedAt/By, UpdatedAt/By) are set by a SaveChanges interceptor from `ICurrentUser` and TimeProvider.
- Audit interceptor: before/after JSON with PasswordHash, SecurityStamp and TOTP keys redacted. It throws on any AuditLog update or delete.
- Initial migration, including SQL triggers that block UPDATE and DELETE on AuditLogs.

**Identity setup**
- Password policy: minimum length 12, complexity, `PasswordHistoryValidator` (last 5).
- Lockout: 5 failed attempts → 15 min.
- `LamsUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser>` is the only `IUserClaimsPrincipalFactory`. It emits only the user id, user name, security stamp and `lams:role_id` claims (the role id is read from `StaffMember.RoleId`). Identity adds `amr` when MFA was used. No role names, permissions, or branch/segment/level go into the cookie. `SecurityStampValidator` refreshes through `SignInManager.CreateUserPrincipalAsync`, so it uses the same factory.
- `LamsSignInManager`:
  - `CanSignInAsync` rejects users whose `StaffMember.IsActive` is false (or who have no staff record).
  - **Preserves the `lams.issued` marker:** `SignInWithClaimsAsync` reads the current application cookie. If it belongs to the same user and has `lams.issued`, the value is copied into the new properties. Otherwise (a fresh login) it is set from TimeProvider. This covers RefreshSignInAsync after a password change or MFA enrolment, and the 2FA completion step.
- Temporary password: `TemporaryPasswordExpiresAt = now + Security:TemporaryPasswordLifetime` (default 72 h) on user creation or admin reset. It is cleared on a successful password change.

**Caching, keys, seeding, options**
- `UserAccessProfileService` (HybridCache) and `AccessCacheInvalidator`:
  - Removes the user key when a user's access attributes change.
  - Removes the role key (`access:role:{roleId}`) when a role's permissions or MFA flag change.
- Data Protection keys stored in EF.
- Idempotent seeders:
  - Roles, permissions, authority limits and branches in every environment.
  - The SYSADMIN account from configuration.
  - Personas in Development only.
- Options validated on start:
  - `Security:AbsoluteSessionLifetime` (default 10 h)
  - `Security:TemporaryPasswordLifetime` (default 72 h)
  - `Security:SecurityStampValidationInterval` (1 min)

**Tests added in this step**
- Enable the skipped architecture test `Identity_account_holds_only_authentication_data`.

**Implementation notes (as built)**
- EF Core cannot model a foreign key between a strongly typed id (`UserId`, `RoleId`) and Identity's `Guid` key. The three such constraints (`StaffMembers.Id → AspNetUsers`, `StaffMembers.RoleId → AspNetRoles`, `AuthorityLimits.RoleId → AspNetRoles`) are added by hand in the migration via `DatabaseObjects.AddManualForeignKeys`; tests prove the database enforces them.
- Read queries that join staff, account, role and branch use the view `vw_UserDirectory` (keyless entity `UserDirectoryRow`, created in the migration).
- `ConsistencyInterceptor` (audit-log guard, `AspNetUserRoles` sync, password history) and `AuditTrailInterceptor` (created/updated stamps, before/after audit records for User/Role/Branch/AuthorityLimit) run inside every SaveChanges. Authentication accounts are not audited as data changes (sign-in bookkeeping); security events are written explicitly.
- Audit log is append-only twice over: the interceptor throws on update/delete, and the `TR_AuditLogs_AppendOnly` trigger rejects UPDATE/DELETE in SQL.
- Password history: the current password + 4 stored previous hashes = the last 5 passwords cannot be reused.
- Seeding (revised before Step 5): reference data is **versioned**. Named, ordered, idempotent seed units (`0001_initial_roles_permissions`, `0002_initial_branches`, `0003_initial_authority_limits`) each run once in their own transaction and are recorded in `SeedHistory` (SeedId, AppliedAt; migration `AddSeedHistory`). After a unit has run, administrator changes are never overwritten (tested: deleting all limits of a type, a branch or a role's permissions survives restarts). Future changes (new role, new grant) are appended as new units; README "Reference data and seeding" documents the steps. Fixed role definitions (name, description, scope, dashboard, attribute rules, administrative flag) stay code-owned and are re-applied on every start. Demo users only when the environment is Development **and** `Seed:DevUsers:Enabled`.
- `.editorconfig` marks `**/Migrations/*.cs` as generated code. `dotnet ef migrations has-pending-model-changes` reports no changes.
- 19 integration tests (Testcontainers SQL Server, one database per test class) cover seeding, the transaction/rollback of the split user model, the role sync, the manual FKs, the audit trail and append-only guards, password history, cookie claims and access-profile cache invalidation.
- Integration: creating a user writes `AspNetUsers`, `StaffMembers` and one `AspNetUserRoles` row in one transaction; a failure in any part rolls back all three; changing `StaffMember.RoleId` leaves exactly one matching `AspNetUserRoles` row.

### Step 4b — [x] View inventory (posted to you before Step 5)
- **Shared:** `_Layout`, `_AuthLayout`, `_Icon`, `_Pager`, `_Modal`, `_Toast`, `_ValidationSummary`, `_SortableHeader`, `_StatusPill`, `_Avatar`. ViewComponents: `Sidebar/Default`, `UserCard/Default` (Sign out is a POST form), `Topbar/Default`.
- **Account:** Login, LoginWith2fa, LoginWithRecoveryCode, ForcedChangePassword, Lockout, AccessDenied, EnableAuthenticator (QR + verify), ShowRecoveryCodes, SessionEnded (shown after a forced sign-out or session expiry).
- **Account/Manage ("My account", from the user card):**
  - Index: profile, with role, branch, level and segment read-only.
  - ChangePassword.
  - TwoFactor: status, enable, disable, reset authenticator.
  - GenerateRecoveryCodes.
  - ResetAuthenticator: confirmation page.
- **Dashboard:** Index, which shows the role-specific name from DashboardKind.
- **Placeholders:** Applications/Index, Packages/Index, Reports/Index, Admin/CreditPolicy/Index, Admin/Pricing/Index, Admin/Settings/Index, sharing a `_ComingSoon` partial.
- **Admin/Users:**
  - Index + `_UserRows` (htmx partial with pager).
  - Create, Edit, and `_UserForm` shared by both.
  - `_RoleAttributeFields`: htmx partial.
  - Details: profile + recent activity, with `_RecentActivity` as an htmx partial.
  - `_TempPasswordModal`.
  - `_ConfirmActionModal`: deactivate, force sign-out, reset password.
  - `_AvailabilityToggle`: htmx inline toggle.
- **Admin/Roles:** Index; Permissions (matrix editor grouped by module, with descriptions; data scope read-only).
- **Admin/AuthorityLimits:** Index (both limit types), Edit, Create (a new currency or level row), `_LimitRow`.
- **Admin/Branches:** Index, Create, Edit, `_BranchForm`.
- **Admin/AuditLog:** Index + `_AuditRows` (htmx partial); Details (before/after JSON side by side).
- **Error:** NotFound (404), ServerError (500), StatusCode (other codes), TooManyRequests (429 from the login rate limiter).

### Step 5 — [x] Web shell
**Pipeline**
- Serilog; `/health` with a DB check.
- IExceptionHandler + ProblemDetails; friendly MVC error pages.
- Security headers: strict CSP with `'self'` only and no inline scripts or styles. htmx gets an `includeIndicatorStyles:false` meta config.
- HSTS; localization (en, plus a ka stub).
- site.css ported from the tokens; fonts and htmx self-hosted.

**Cookie**
- Name `__Host-Lams.Auth`: Secure, Path=/, no Domain, HttpOnly, SameSite=Strict.
- Sliding expiration of 30 minutes; no persistent cookie.
- The cookie handler's `TimeProvider` is the app's TimeProvider, so tests can swap in `FakeTimeProvider`.

**`LamsCookieEvents.OnValidatePrincipal`**
1. It does **not** replace Identity's validation. It first calls `SecurityStampValidator.ValidatePrincipalAsync(context)`.
2. If the principal was rejected (`context.Principal` is null or not authenticated), it returns at once.
3. Only then does it check the absolute lifetime from `Properties.Items["lams.issued"]` using `SessionLifetimePolicy`. If the marker is missing, the session is expired (fail closed). An expired session is rejected, signed out, and the Session expired event is audited.

**htmx-aware redirects (`OnRedirectToLogin` / `OnRedirectToAccessDenied`)**
- With `HX-Request: true`:
  - Unauthenticated or expired session → 401 with an empty body and `HX-Redirect: /Account/Login?returnUrl=<local path taken from HX-Current-URL>`.
  - Forbidden → 403 with `HX-Redirect: /Account/AccessDenied`.
- Without htmx, the normal 302 redirect.

**Antiforgery**
- Cookie `__Host-Lams.Af`, `HeaderName = "RequestVerificationToken"`, and a global `AutoValidateAntiforgeryTokenAttribute`.
- The layout puts the token in a `<meta>` tag. `site.js` adds it to every non-GET htmx request in `htmx:configRequest`.

**UI**
- Layout with sidebar, topbar and user card; the icon partial.
- Policy provider and handlers; the Tag Helper; placeholder pages.

**Architecture test update**
- The "controllers do not use DbContext" test also asserts that it found **at least one** controller type, so it can never pass vacuously.

**Implementation notes (as built)**
- Every endpoint requires a signed-in user (authorization fallback policy) unless marked `[AllowAnonymous]` (error pages, `/health`; static files are served before authentication).
- `CurrentAccess` (scoped) resolves the cached `UserAccessProfile` once per request; the permission handler, the administration handler, the resource handlers, the `asp-permission` tag helper and the sidebar all use it.
- Sidebar items are defined once in `Navigation/Menu.cs` (visibility = permission predicates; Dashboard hidden for the administrative dashboard kind). "Create Application" uses `asp-permission="applications.create"`.
- No inline scripts or styles anywhere (CSP `'self'` only; avatars use colour classes). htmx config: no injected indicator styles, no eval, no script tags, no history cache, self requests only.
- htmx 401/403 responses disable the status-code page middleware, so the body stays empty (found by the test).
- Prototype search box and notification bell are not rendered: they would be non-functional in Phase 1.
- Placeholders for every navigation target (workspace and admin). Admin Users/Roles/AuthorityLimits/Branches/AuditLog are replaced in Step 7.
- Fonts are the Latin subset; Georgian text falls back to the system font. `SharedResource.ka.resx` is empty (falls back to English).
- Tests: web smoke tests with `WebApplicationFactory` (health, security headers, anonymous redirect, htmx 401 + HX-Redirect + empty body, foreign HX-Current-URL ignored, 404 page, static assets), menu visibility per role, policy provider / handlers / tag helper, resource keys used by views.

### Step 6 — [x] Account
**Login**
- Username or email, with a rate limiter on the login POST (fixed window per IP).
- **returnUrl** is used only if `Url.IsLocalUrl` accepts it, via `LocalRedirect`; otherwise the user goes to the dashboard.
- Unknown user, wrong password and inactive user all show **one generic message**, "Invalid user name or password." The audit log records the real reason.

**Lockout**
- We keep Identity's default behaviour: `PasswordSignInAsync` returns LockedOut **before checking the password**. The Lockout page is therefore shown whenever the account is locked, whatever password was entered. This is acceptable for an internal staff system and is documented in the README.

**Temporary password expiry**
- Checked after the password is verified.
- An expired temporary password fails with "Your temporary password has expired. Contact your administrator."
- Audited as `LoginFailed:TemporaryPasswordExpired`.

**Logout**
- `[HttpPost, ValidateAntiForgeryToken]` only; no GET route.
- Calls `SignInManager.SignOutAsync`, which clears the auth and 2FA cookies.
- Writes a `Logout` audit event and redirects to Login.

**Other**
- Forced password change; it re-issues the cookie with the original `lams.issued` preserved.
- TOTP MFA: QR via QRCoder as SVG, a 2FA login step, recovery codes. Enrolment is enforced when the role or the user requires MFA.
- The "My account" pages.
- Every security event is audited, and LastLoginAt is set.
- Post-login redirect by DashboardKind; SYSADMIN goes to Admin › Users. Access denied page.

**Implementation notes (as built)**
- `AccountService` (Infrastructure/Identity) holds sign-in, 2FA, logout, password change and MFA management, with an audit record per security event (`LoginSucceeded`, `LoginFailed` with reason `UnknownUser`/`InvalidPassword`/`NotAllowed`/`InvalidAuthenticatorCode`/`InvalidRecoveryCode`, `LockedOut`, `LoginFailed:TemporaryPasswordExpired`, `Logout`, `PasswordChanged`, `MfaEnabled`, `MfaDisabled`, `MfaReset`, `RecoveryCodesGenerated`). Controllers stay thin.
- Sign-in order: find by user name or email → `CheckPasswordSignInAsync` (lockout first, then password; inactive staff → `NotAllowed`) → temporary-password expiry → `LamsSignInManager.SignInAfterPasswordCheckAsync` (cookie, or the 2FA step). Never persistent.
- `ForcedAccountSetupFilter` (global): temporary password → `/Account/ChangePassword`; MFA required by role **or** account and not enrolled → `/Account/EnableAuthenticator`. Exempt endpoints carry `[AllowDuringAccountSetup]`. The state (`AccountSecurityState`) comes from the cached access profile: the user cache entry now also holds MustChangePassword, TwoFactorEnabled and the account's RequireMfa (loaded from `vw_UserDirectory` in one query); the role entry holds the role's RequireMfa. Admin password reset / MFA requirement and self-service password or MFA changes invalidate the user entry after commit.
- QR code: inline SVG built from the QRCoder module matrix (no style attributes, CSP-safe).
- Sign-in rate limit: fixed window per client IP, `Security:SignInAttemptsPerMinute` (default 10) on the password, 2FA and recovery-code posts; rejected requests get the 429 page.
- Administrators land on `/Admin` (Users is the area's default controller).
- View inventory deviation: no separate `SessionEnded` page; an expired or ended session simply returns to the sign-in page (with the return URL). The Lockout, AccessDenied, ChangePassword (forced), EnableAuthenticator and ShowRecoveryCodes pages exist; "My account" has Index, ChangePassword, TwoFactor and ResetAuthenticator.
- Tests (17 web + 1 unit): sign-in by user name and email, success audit and LastLoginAt, generic failure message for wrong password and unknown user (audited with reasons), inactive user, lockout after 5 failures (lockout page also with the correct password), forced password change for the seeded SYSADMIN and landing on Admin › Users, reuse and wrong-current-password errors, expired temporary password and re-issue, local vs external return URLs, logout (405 for GET, 400 without token, cookie cleared, audited), cookie flags, UW denied the admin area with no admin menu / Create button, role-required MFA end to end with real TOTP codes, MFA cannot be turned off when required, My account page, rate limit (429).

### Step 7 — [x] Admin area
Done in five sub-steps, each with a green build and tests, its own commit and a PLAN.md update:
7a Users · 7b Roles & permissions · 7c Authority limits · 7d Branches · 7e Audit log.

- **Users:**
  - htmx list (search, filters, sort, paging) styled like the team roster.
  - Create/edit with field rules driven by the role.
  - Actions: activate/deactivate, unlock, reset temporary password (shown once), force sign-out (updates the security stamp), availability toggle, enable/require MFA, recent activity.
  - Every access-attribute change goes through the single access-change step from Step 3. Business fields are edited on `StaffMember`; account actions (unlock, reset password, MFA, force sign-out) go through `IIdentityService`.
- **Roles:** matrix editor with SoD enforced.
- **Authority limits, Branches, Audit log:** the editor, CRUD with deactivate-instead-of-delete, and the read-only viewer.
- **Guard rails:** no self-deactivation or self-demotion; at least one active SYSADMIN; the SYSADMIN role keeps `admin.users.manage` and `admin.roles.manage`.

#### Step 7a — [x] Users (+ UI fixes)
**As built**
- `UsersController` (Admin area; class: `admin.users.view` **or** `admin.users.manage`; every write: `admin.users.manage`). Thin: all rules stay in `UserAdministrationService`.
- **List** (`Index` + `_UserRows`): search (name, user name, email), role / branch / status filters, sortable columns (`aria-sort`), pager (`_Pager` + `PagerModel`, shared). The filter form is a normal GET form; with htmx it loads only `Rows` (`HX-Push-Url` keeps the address bar bookmarkable; `/Admin?...`, because Users is the area's default controller). `Rows` without htmx redirects to the full page. Roster look: avatar, name + email, "You" chip, role + level/segment, branch, status pill, "Working today" switch, last sign-in.
- **Create / Edit** (`_UserForm`, `_RoleAttributeFields`): changing the role reloads the branch / level / segment fields by htmx (`RoleFields`); Required → required field, Optional → "No …" option, NotAllowed → not shown. Deactivated branches stay selectable only for a user who already has them. Edit warns that access changes sign the user out.
- **Details**: profile, sign-in and security (temporary password and expiry, MFA enrolled / required by role / required for user, lockout), actions, recent activity (`_RecentActivity`, last 10, htmx refresh).
- **Actions**: deactivate, force sign-out and reset password are confirmed in a modal (`Confirm` → `_ConfirmActionModal` by htmx; a page without JavaScript); activate, unlock and require / stop requiring MFA are direct POSTs; the availability switch is a `<button role="switch">` form that swaps itself by htmx (toast via `HX-Trigger`) and works as a plain POST without JavaScript. Deactivate is hidden on your own page (the service also refuses it).
- **Temporary password** (create and reset) is rendered once — as a page after create, as a modal after an htmx reset — with `Cache-Control: no-store`, a copy button and the expiry; never stored or put in TempData.
- Service change: create / update now check the role-driven attributes even when other fields are invalid, so the form shows **every** error at once (unit test added).
- `HtmxExtensions.ShowToast` (ASCII-safe JSON in `HX-Trigger`) and `PushUrl`; `DisplayFormat` (dates, money with ₾ / $ / €); `LocalizerExtensions.AuditAction` (`AuditAction.{action}`, data changes as `AuditAction.{entity}.{action}`, fallback to the stored name).

**UI fixes**
1. User card: role and level on their own line, never truncated ("Senior Underwriter", resource `UserCard.RoleWithLevel` = `{1} {0}`); segment / branch on a third line, truncated with a `title` tooltip; the name has a tooltip too.
2. Dashboard chip includes the level: `Sr UW · Tamta Tatishvili` (upper-cased by CSS, as in the prototype). Short level names are resources (`AuthorityLevel.{level}.Short`: Jr / Std / Sr).
3. Dashboards of users who hold `applications.decide` (Underwriter, Branch) show the actual limit: "Authority limit: ₾50,000" (one entry per currency with a limit; "none set, so every decision is escalated" when no row matches). New `IAuthorityLimitEvaluator.ListCreditDecisionLimitsAsync` uses the same matching rule as the evaluation (unit tests per level, BM, non-deciders).

**Tests (17 web + 7 unit):** list + htmx rows + push URL, rows redirect without htmx, status filter + sort, create Senior UW → one-time password (no-store) signs in and is forced to change it, every field error at once, role fields per role (UW / HD / SYSADMIN), edit branch rotates the stamp, deactivate via modal + audit, no self-deactivation, htmx password reset modal, unlock + force sign-out + require MFA (+ audit), availability switch + toast, UW gets Access denied on a user action, dashboard chip / limit / user card for UW, BM and LO; resource keys for audit actions, confirm texts, status filters and short levels.

#### Step 7b — [x] Roles & permissions
**As built**
- `RolesController` (`admin.roles.manage`), over the existing `RoleAdministrationService` (SoD, required administrator permissions, audit `RolePermissionsChanged` with before/after, role cache invalidated after commit).
- **Index**: role table (name, code, description, data scope, dashboard, users, permission count, MFA) and the read-only **permission matrix** (permissions grouped by module × the 10 roles, ✓ per grant, sticky first column; each role column links to its editor). Business roles first, the administrator last.
- **Permissions/{id}** (editor): read-only role profile (data scope with explanation, dashboard, branch / level / segment rules, user count); MFA requirement for the role (POST `RequireMfa`); permissions per module with name, code and description and an "x of y granted" count. Not assignable under SoD → disabled with "Not for this role"; the administrator's `admin.users.manage` / `admin.roles.manage` → checked, disabled, "Required" and sent as hidden inputs. Sticky save bar ("applies on the next page load, without signing out").
- On a failed save (e.g. a crafted post with an admin permission for a business role, or without a required administrator permission) the page shows the error and keeps the administrator's selection; nothing is saved.
- Texts: permission and module names use `Permission.{code}` / `PermissionModule.{key}` resources when present, else the catalog's English text; new resources for data scopes (+ descriptions) and attribute rules.

**Tests (7 web):** matrix lists the 10 roles and exactly the seeded grants; editor disables admin permissions for UW and locks the administrator's two; removing `reports.view` from UW hides Reports for a signed-in UW on the next request (and `/Reports` is denied) + audit; SoD violation rejected with the selection kept; administrator must keep user management; requiring MFA on a role sends its signed-in users to authenticator set-up; UW gets Access denied on `/Admin/Roles`. Resource test covers data scopes and attribute rules.

#### Step 7c — [x] Authority limits
**As built**
- `AuthorityLimitsController` (`admin.authority-limits.manage`) over the existing `AuthorityLimitAdministrationService` (business roles only, the role's level rule, no duplicates, value ranges). Data changes are audited by the interceptor (`AuthorityLimit` Created / Updated / Deleted). Limits are not cached, so a change applies to the next evaluation and the deciders' dashboards at once.
- **Index**: credit decision limits (role, level or "All users of the role", currency, amount as ₾ / $ / €) and pricing exception limits (role, "Below x pp", "At least y%", "No limit" for empty thresholds), each with Edit and Delete; hints explain escalation when no row matches and routing to the Director.
- **CreditForm / PricingForm** (create and edit share one view per type). Create: role (business roles; roles with levels are marked "by level"), level, currency, amount / thresholds. Edit: role, level and currency identify the row and are shown read-only; only the amount (or the thresholds) change.
- **Delete** asks for confirmation through a new generic `_ConfirmModal` / `Confirm` (shared `ConfirmModel`: modal with htmx, page without JavaScript), explaining the consequence (escalation / Director). Limits are deleted, not deactivated: a missing row is itself a meaningful rule (escalate).
- View inventory deviation: no inline `_LimitRow` editing; full-page forms are simpler and work without JavaScript.

**Tests (10 web):** seeded limits of both types shown; raising UW Senior to 60,000 shows on a signed-in UW's dashboard at once + audit; a USD row adds "$20,000" next to "₾50,000"; rules (UW without level, BM with level, duplicate, administrator role) rejected with the message and nothing saved; delete via the confirm modal → UW dashboard says "none set"; clearing the HD rate-gap threshold stores "no limit"; UW gets Access denied.

#### Step 7d — [x] Branches
**As built**
- `BranchesController` (`admin.branches.manage`) over the existing `BranchAdministrationService`; data changes audited by the interceptor (`Branch` Created / Updated / Deleted).
- **Index**: code, name, city, number of assigned users (links to the Users list filtered by the branch), status; actions Edit, Deactivate (confirm modal stating how many users keep the branch) / Activate, and Delete — offered **only** when no user is assigned (the service refuses it otherwise; the error is shown).
- **Form** (create and edit share one view): the code is entered once (stored upper-case, 2–10 letters, digits, hyphens) and shown read-only on edit; a posted code on edit is ignored. A duplicate code is shown next to the code field.
- A deactivated branch disappears from the user form's branch list, but users who have it keep it (it stays selectable for them, marked "(inactive)") — as built in 7a.
- Known effect: a renamed branch shows its new name in the sidebar user card after the 1-minute cache expiry (the name is part of the cached user entry; access is not affected).
- View inventory: `_BranchForm` became one `Form` view (no partial needed).

**Tests (7 web):** list of the 6 seeded branches; create stores an upper-case code + audit; duplicate code rejected next to the field; edit changes name / city but not the code; deactivation (modal shows the user count) hides the branch from new assignments while an assigned user keeps it; delete offered and done only for an unused branch; UW gets Access denied.

#### Step 7e — [x] Audit log
**As built**
- `AuditLogController` (`admin.audit.view`): `Index`, `Rows` (htmx) and `Details` — GET only; there is no endpoint that changes or deletes records (append-only also in the interceptor and the SQL trigger).
- **Index** (`_AuditRows`, shared `_Pager`): newest first; filters for free text (user name or record id), action, subject type (both drop-downs filled from the data, `IAuditLogQueries.ListEntityTypesAsync` added), from / to dates (whole local days, both inclusive) and the acting user (`?user=`, shown as a chip; linked from a user's Recent activity card and from a record's details). The action filter uses the query key `event`, because `action` is reserved by MVC routing (it was dropped from generated URLs). Columns: when, action (display name), by ("System" when nobody was signed in), subject (type + short id, linked to the user / branch / role / limits page where one exists), IP.
- **Details**: record metadata (local and UTC time, actor with "all actions by this user", subject and full id, IP, browser, request id) and **what changed**, prepared by `AuditChanges`: entity changes as a field table (Before / After, changed rows highlighted), lists such as role permissions as Removed / Added, anything else (security event detail) as text.
- View inventory deviation: no separate `_AuditFilters` partial; the before / after view is a table instead of two raw JSON panes (raw JSON is still shown when the data is not an object or list).

**Tests (7 web + 4 unit):** action filter with htmx (rows + push URL); date filter by local day (today / tomorrow / yesterday); a user edit shows FullName before → after; a permission change shows removed `reports.view` and added `applications.tag`; actor filter; POSTs to delete / edit / details are 404/405 and the count is unchanged; UW gets Access denied; `AuditChanges` unit tests (object table, created entity, list diff, plain value / empty).

**Step 7 complete.** All five admin screens replace their placeholders; CreditPolicy, Pricing and Settings stay placeholders in Phase 1.

### Step 8 — [x] Integration tests and docs (light)
**Scope agreed with the user (2026-10-06):** the app runs only locally, so Step 8 adds only the tests that were still
missing for the definition-of-done scenario and the permission-change-without-logout case, and finishes the README.
The list below is the original plan; the inventory after it records what exists.

**Inventory**
- *Already covered by earlier steps:* login success / failure, generic message, inactive user (`AccountTests`); forced
  password change (`AccountTests`, seeded SYSADMIN); admin area denied for non-admins (`AccountTests` + one test per
  admin screen); menu filtering (`MenuTests` per role + UW page check); **permission change on the next request
  without logout** (`RoleAdministrationTests.Removing_a_permission_applies_on_the_next_request_without_signing_out`:
  a signed-in UW loses Reports after the admin edits the role in the UI, `/Reports` is denied, the session stays
  valid, audit written — so no new test was needed); lockout after 5 failures with the lockout page for correct and
  wrong passwords; anonymous htmx 401 + `HX-Redirect` + empty body, foreign `HX-Current-URL` ignored, 302 without
  htmx (`WebShellTests`); logout (405 GET, 400 without token, cookie expired, audit); temporary password within 72 h
  (forced change) and after 72 h + 1 min (expiry message, audit, works again after an admin reset); branch change
  rotates the security stamp (`UserAdministrationTests`); profile cache invalidation (`AuditAndSecurityPersistenceTests`).
- *Added in Step 8:* `DefinitionOfDoneTests` — entirely through the UI: seeded `sysadmin` signs in, changes the
  password, lands on `/Admin`, creates "Tamta Tatishvili" as Senior UW in Vake; she signs in with the one-time
  password, is forced to change it, sees exactly Dashboard / Applications / Packages / Reports, no Create Application,
  the "Sr UW" chip, ₾50,000 and "Senior Underwriter"; `/Admin/Users` → Access denied, and an htmx admin request as
  UW → 403 with `HX-Redirect: /Account/AccessDenied` (the htmx-403 case was not covered before).
- *Deliberately not done (local single-instance app):* lockout expiry after 15 min; segment / level change + cookie
  refresh after +61 s without branch / segment / level claims; session lifetime scenarios (sliding 30 min, absolute
  10 h, password change at 9 h 50 m, tampered `lams.issued`); forced sign-out / deactivation taking effect after the
  stamp interval; htmx POST with / without antiforgery header. The underlying rules have unit tests
  (`SessionLifetimePolicy`, `TemporaryPasswordPolicy`, access-change step) and the code paths are in place.
- *Docs:* README now has how to run (Docker SQL Server, user-secrets, `dotnet run`, tests), the accounts (sysadmin and
  the 14 demo personas with role, branch, level / segment and limit), a definition-of-done walkthrough, security
  behaviour (sessions, lockout note, MFA, audit, proxy note), the open questions and the assumptions.

**Original plan (for reference)**
Integration tests use Testcontainers, WebApplicationFactory and FakeTimeProvider.

**Core**
- Login success and failure.
- Forced password change.
- Admin area denied for non-admins.
- Menu filtering.
- A role permission change shows up on the next request without logout.
- The definition-of-done scenario: create a Senior UW in Vake, log in as that user, check the menu, get denied on Admin.

**Lockout (default Identity behaviour)**
- After 5 wrong passwords, the account is locked.
- A further attempt shows the Lockout page with the **correct** password, and also with a wrong one.
- After 15 min + 1 s (FakeTimeProvider), login works again.

**Access attributes (item 1)**
- An admin changes a logged-in UW's Branch. Then the same for Segment on an HD and AuthorityLevel on a UW.
- Each time, the next profile read shows the new value and the security stamp has changed.
- After the validation interval (+61 s), the old cookie is refreshed with the same claims set; no branch, segment or level claims appear.

**htmx (item 3)**
- An htmx GET to an admin partial without a cookie returns 401, `HX-Redirect` to Login with an encoded local returnUrl, and an empty body.
- The same request as UW returns 403 with `HX-Redirect` to AccessDenied.
- The same request without the `HX-Request` header returns a 302.
- An htmx POST without the antiforgery header returns 400; with the header it succeeds.

**Session lifetime (item 5)**
- Activity every 20 min keeps the session alive past 30 min.
- 31 min of inactivity ends the session.
- At 10 h + 1 min, even with constant activity, the session ends.
- **Password change at 9 h 50 m** (cookie re-issued) → the session still ends at 10 h, and `lams.issued` is unchanged.
- A cookie whose `lams.issued` was removed (properties tampered via the test ticket helper) is rejected (fail closed).
- **Stamp validation still works after the lifetime check is added:**
  - An admin force-signs-out a logged-in user. After the validation interval (+61 s), the next request goes to Login.
  - The same for deactivation.
  - Before the interval, the request still succeeds, which proves the interval is respected.

**Logout (item 6)**
- A GET to `/Account/Logout` returns 404/405.
- A POST without a token returns 400.
- A POST with a token expires the cookie (`Set-Cookie` with a past date), the next request redirects to Login, and a Logout audit row exists.

**Temporary password (item 8)**
- Login within 72 h goes to the forced password change.
- At 72 h + 1 min, login fails with the expiry message and an audit row exists.
- After an admin reset, login works again.

**Docs**
- README: run instructions, seeded accounts, open questions, assumptions, and the note on lockout behaviour.

## 5. Assumptions (accepted)
1. **Missing currency limit.** CreditDecision limits are seeded in GEL only. If no limit row matches, the result is **Escalate**; NotAuthorized applies only when the user lacks `applications.decide`.
2. **HD thresholds.** The rate gap rule is strict (< 2.0 pp) and the ROAE rule is inclusive (≥ 15%).
3. **MFA required.** MFA is required if the role or the user flag requires it. The admin's "enable MFA" means "require enrolment at next login".
4. **SYSADMIN role.** It must keep `admin.users.manage` and `admin.roles.manage`.
5. **Self-hosted assets.** Fonts and htmx are self-hosted, so the CSP stays `'self'` only.
6. **Persona accounts.**
   - Usernames are `firstname.lastname`; emails follow the prototype pattern, e.g. `mariam.l@pcb.ge`.
   - Branches follow the prototype roster.
   - Dev personas do not need to change their password; the SYSADMIN does.
7. **Migrations.** Applied at startup only when `Database:ApplyMigrationsOnStartup=true` (Development). Seeding is idempotent and runs on every start.
8. **Application services.** One per feature, with no command dispatcher.
9. **Georgian resources.** English texts only; a `ka` culture is registered with English fallback.
10. **Lockout page.** It is shown whenever the account is locked (Identity default), so it can reveal that a locked account exists. Accepted for an internal staff system.

## 6. Open questions (seeded as instructed, all editable in the role matrix)
1. BDM keeps documents.view, upload and delete-own-new (FRD 6.4) despite being "read-only" (FRD 5.3).
2. BO has DataScope = Organization; queue filtering is deferred.
3. `applications.reassign` is defined and granted to no role.
4. mark-priority, tag, add-internal-note, repull-credit-data, reverify-income, print and archive are defined and granted to no role.
5. Credit Committee and CRO (Flow C) are not created as roles; this is flagged in the README.

## 7. Verification
- `dotnet build` with 0 warnings. `dotnet test` all green; Docker must be running for the integration tests.
- Manual check:
  1. Run `docker compose up -d`.
  2. Set user-secrets: connection string, SYSADMIN credentials, dev password.
  3. Run `dotnet run --project src/Lams.Web`.
  4. Log in as SYSADMIN, change the password, and land on Admin › Users.
  5. Create a Senior UW in Vake.
  6. Log in as that user in another browser and change the password. The sidebar shows Dashboard, Applications, Packages and Reports only. `/Admin/Users` shows Access denied.
  7. As admin, remove `reports.view` from UW. Within one minute, with no logout, Reports disappears for that user.
- `/health` returns Healthy.
