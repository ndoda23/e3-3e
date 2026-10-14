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
| 8 — Integration tests and docs (light) | done | `67435b9` |

Test status after Step 8: build with 0 warnings; 798 tests green (Domain 135, Application 519, Architecture 9, Integration 135 — Testcontainers SQL Server).

The Phase 1 commits above are on branch `old-main-with-docs`; the Phase 2 branch `phase2-creditflow` starts from the squashed commit `b77ef6a` ("Phase1") of the same code.

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
- Post-login landing by DashboardKind (since the UI polish SYSADMIN lands on the administration dashboard, not Admin › Users). Access denied page.

**Implementation notes (as built)**
- `AccountService` (Infrastructure/Identity) holds sign-in, 2FA, logout, password change and MFA management, with an audit record per security event (`LoginSucceeded`, `LoginFailed` with reason `UnknownUser`/`InvalidPassword`/`NotAllowed`/`InvalidAuthenticatorCode`/`InvalidRecoveryCode`, `LockedOut`, `LoginFailed:TemporaryPasswordExpired`, `Logout`, `PasswordChanged`, `MfaEnabled`, `MfaDisabled`, `MfaReset`, `RecoveryCodesGenerated`). Controllers stay thin.
- Sign-in order: find by user name or email → `CheckPasswordSignInAsync` (lockout first, then password; inactive staff → `NotAllowed`) → temporary-password expiry → `LamsSignInManager.SignInAfterPasswordCheckAsync` (cookie, or the 2FA step). Never persistent.
- `ForcedAccountSetupFilter` (global): temporary password → `/Account/ChangePassword`; MFA required by role **or** account and not enrolled → `/Account/EnableAuthenticator`. Exempt endpoints carry `[AllowDuringAccountSetup]`. The state (`AccountSecurityState`) comes from the cached access profile: the user cache entry now also holds MustChangePassword, TwoFactorEnabled and the account's RequireMfa (loaded from `vw_UserDirectory` in one query); the role entry holds the role's RequireMfa. Admin password reset / MFA requirement and self-service password or MFA changes invalidate the user entry after commit.
- QR code: inline SVG built from the QRCoder module matrix (no style attributes, CSP-safe).
- Sign-in rate limit: fixed window per client IP, `Security:SignInAttemptsPerMinute` (default 10) on the password, 2FA and recovery-code posts; rejected requests get the 429 page.
- Administrators land on their dashboard (locked users, users without MFA, role/permission changes); Admin › Users is one click away.
- View inventory deviation: no separate `SessionEnded` page; an expired or ended session simply returns to the sign-in page (with the return URL). The Lockout, AccessDenied, ChangePassword (forced), EnableAuthenticator and ShowRecoveryCodes pages exist; "My account" has Index, ChangePassword, TwoFactor and ResetAuthenticator.
- Tests (17 web + 1 unit): sign-in by user name and email, success audit and LastLoginAt, generic failure message for wrong password and unknown user (audited with reasons), inactive user, lockout after 5 failures (lockout page also with the correct password), forced password change for the seeded SYSADMIN and landing on the administration dashboard, reuse and wrong-current-password errors, expired temporary password and re-issue, local vs external return URLs, logout (405 for GET, 400 without token, cookie cleared, audited), cookie flags, UW denied the admin area with no admin menu / Create button, role-required MFA end to end with real TOTP codes, MFA cannot be turned off when required, My account page, rate limit (429).

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
  password, lands on the administration dashboard, creates "Tamta Tatishvili" as Senior UW in Vake; she signs in with the one-time
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
  4. Log in as SYSADMIN, change the password, and land on the administration dashboard (locked users, users without MFA, role/permission changes); open Admin › Users from the sidebar.
  5. Create a Senior UW in Vake.
  6. Log in as that user in another browser and change the password. The sidebar shows Dashboard, Applications, Packages and Reports only. `/Admin/Users` shows Access denied.
  7. As admin, remove `reports.view` from UW. Within one minute, with no logout, Reports disappears for that user.
- `/health` returns Healthy.

---

# Phase 2 — CreditFlow

**Status: plan approved (2026-10-12). Implementation follows the steps in §P.7.**

Sources, in order of authority: `docs/CreditFlow-task.md` (the bank's task; every bullet is a requirement) →
`docs/` (FRD, UI reference: look and feel only) → Phase 1 code, this file and README. Time box: **2 days**.

**Approval decisions (2026-10-12):**
1. The stray working-tree edits were the user's own notes → reverted in P0; the `.editorconfig` naming rules stay.
2. Full rename `Lams` → `CreditFlow` (P1), before any new code.
3. Every number and decision in §P.4 is approved as written.
4. Time box is 2 days, not 3. The UI is not left for the end: P10 and P11 include the minimal pages, so the end-to-end
   flow (application → PreCheck → scoring → decision → contract → disbursement) works in the browser as early as
   possible. P13 then polishes those pages. If time runs short, follow the cut list.
5. Seeded staff, enough for four-eyes: at least 2 Underwriters, 2 Senior Underwriters, 3 Credit Committee members,
   1 Compliance officer, 1 Auditor, and Operators in 2 branches.
Deviation agreed with the bank in writing: **EF Core instead of Dapper** (recorded first in README "Decisions").

Working tree found at the start (not committed, not by me): `.editorconfig` gained PascalCase rules for
`private const` / `private static readonly` (this is the "known small issue" fix — kept), and six stray edits:
a `/// in the future.` line inside a method body in `SessionLifetimePolicy` (CS1587 → the build fails under
`TreatWarningsAsErrors`), `/// matrix` above `RoleAdministrationService`, a `// not loged in/out !` comment on
`StaffAccess`, the summary removed from `IdGenerator`, a comment removed from `Permissions`, and the CreditPolicy menu
item removed from `Menu.cs`. Step P0 reverts the stray edits (the menu item and `Permissions` lines disappear anyway in
P2). Confirmed by the user: these were notes, reverted.

## P.1 Gap analysis (task bullet → exists / change / new)

| Task bullet | State | What happens |
|---|---|---|
| .NET 8+, C#, nullable | exists | stay on .NET 9 |
| MS SQL, ORM Dapper | change | EF Core (agreed with the bank); README decision #1 |
| Consistent git history | exists | one focused commit per step below |
| Ambiguities in README | new | §P.4 → README "Ambiguities and decisions" |
| Mocks behind interfaces, configurable scenarios | new | §P.6; scenarios by config + seeded personal numbers |
| Sanctions/PEP list in DB or file | new | `SanctionsListEntries` table (seeded) |
| Credit bureau mock incl. unavailable + timeout | new | scenario per personal number; retry in background |
| KYC / ID verification mock; operator enters ID data | new | `IIdentityVerificationService` |
| Collateral valuation/registration/insurance entered manually | new | collateral fields + conditions on the application |
| FX rates table, admin edit or file import | new | `FxRates` + admin screen + CSV import |
| Core banking mock with tx id, error, timeout | new | idempotent by key (the mock stores processed keys) |
| Email/SMS written to DB or log | new | `Notifications` table + log line |
| Contract signing = operator marks signed | new | `MarkContractSigned` |
| 8-step process, all statuses | new | `LoanApplication` state machine (§P.3.2) |
| Cancel any time before disbursement; no other transitions; history who/when/why | new | inside the aggregate |
| Personal number 11 digits, unique; age ≥18 / ≤75 at last installment | new | `PersonalNumber` value object, unique index, scoring rules + submit validation |
| ID document valid on disbursement day | new | checked in `Disburse` |
| PEP/sanctions match blocks until compliance decision | new | `ComplianceReview` |
| One open application per client per product | new | domain check + filtered unique index |
| Co-borrower and guarantor: same KYC and scoring | new | parties on the application (§P.4 #4) |
| Products consumer/mortgage/auto, min/max amount, term, rate range, admin edits | new | `LoanProduct` with versions |
| Product snapshot at submission | new | owned snapshot on the application |
| GEL/USD/EUR; FX loan below threshold only with same-currency income | exists (enum) / new | `Currency` moves to Domain/Common; scoring rule |
| Collateral: mortgage mandatory LTV ≤80%, auto ≤70%, valuation ≤6 months | new | product collateral rules + scoring rules |
| Document checklist by product and amount; no scoring without it | new | product version checklist; guard in `StartScoring` |
| PTI with income-dependent threshold, +20% FX stress | new | scoring rule `PTI` with bands |
| Bureau 30+ DPD in 12 months → auto reject; retry when unavailable | new | rule `BUREAU_DPD30`; outbox retry with backoff |
| Scoring rules in DB, versioned; per-rule result and reason stored | new | `ScoringRuleSet` versions; `ScoringRuns`/`ScoringRuleResults` |
| Total exposure incl. guarantees ≤ limit | new | rule `EXPOSURE` + locked re-check at disbursement |
| Authority ≤20k UW, ≤100k Senior UW, >100k committee (2 of 3), configurable | change | replaces the Phase 1 authority-limit matrix with `ApprovalThresholds` |
| Four-eyes; one decision per person per application | new | domain rule + unique index (ApplicationId, DecidedBy) |
| Counter-offer (amount/term/rate), client consent, re-scoring | new | `CounterOffered` → `Scoring` |
| Reason mandatory on rejection and override | new | domain validation |
| Escalation after 2 working days in Underwriting | new | `WorkingDayCalendar` + `Holidays` + job |
| Disbursement only after signed contract + all conditions | new | state machine |
| Annuity / differentiated schedule, EIR incl. fees, rounding to last installment | new | `ScheduleCalculator`, `Money` |
| Idempotent disbursement | new | §P.3.5 |
| Early repayment partial/full, recalculation, product penalty | new | `Loan.Repay*` |
| Layered or modular monolith, justified | change | README: layered Clean Architecture with feature folders (§P.2.7) |
| Domain ← Application ← Infrastructure ← Web | exists | kept |
| Repository per aggregate root, interface in Domain | change | §P.2.2 |
| Unit of Work, one transaction | exists | `IUnitOfWork.ExecuteInTransactionAsync`; audit + outbox in the same transaction |
| Rich domain; scoring and schedule in separate testable services | new | pure domain services |
| Thin controllers, validation, Result | exists | kept |
| Side effects through transactional outbox with retry | new | `OutboxMessages` + dispatcher |
| Architecture tests | exists / change | add: repository interfaces in Domain, EF only in Infrastructure (also Web) |
| Clients: registration, KYC, incomes, obligations | new | Clients screens |
| Products with versions | new | Admin › Products |
| Multi-step form, upload, status history | new | Applications screens |
| Rule engine, mock bureau, explanation | new | scoring tab on the application |
| Inbox per role, counter-offer, committee voting | new | Underwriting / Committee / Compliance inboxes |
| Contract + schedule PDF, gapless unique numbers `LN-2026-000123` | new | `ContractNumbers` row lock; PDFsharp-MigraDoc |
| Notifications: status change, escalation, offer expiry | new | outbox handlers → `Notifications` |
| Reports: portfolio by product, approval rate, avg time per stage, rejection reasons; Excel/CSV | new | Reports + ClosedXML + CSV writer |
| Auth method justified; secure passwords; failed-login limit | exists | Identity cookies, PBKDF2 hashes, lockout + rate limit (README section) |
| Roles: Operator, Underwriter, Senior Underwriter, Credit Committee, Compliance, Admin, Auditor | change | §P.2.1 |
| Branch scope, underwriter never sees own application for approval, Auditor read-only | change | §P.2.4 |
| Permissions in DB, runtime-editable | exists | kept unchanged |
| Lists hide inaccessible records; no IDOR | change | SQL-side filters + by-id re-check (§P.2.4) |
| Logs in MS SQL with time, level, message, exception, user, request path | change | Serilog MSSqlServer sink (§P.2.5) |
| Audit table: status, scoring, approve/reject, overrides, disbursement, role changes | change | extend existing audit (role changes exist) |
| Mask personal number, passwords, sensitive data in logs | new | masking sink + audit redaction |
| Parameterised, no N+1, server-side paging/sort/filter | exists (admin) / new | same pattern for all new lists |
| decimal, one rounding rule | new | `Money` (§P.4 #14) |
| rowversion: concurrent votes not lost, two UWs cannot both approve | new | §P.3.5 |
| Exposure limit without race | new | `sp_getapplock` per party inside the disbursement transaction |
| Transactions via UoW; indexes for queues and reports | new | §P.3.3 |
| Seed: products, users of all roles, applications in various statuses | change | new seed unit + dev data seeder |
| Idempotent background jobs (escalation, offer expiry, outbox) | new | `BackgroundService`s, `UPDLOCK, READPAST` claiming |
| Unit tests: transitions, scoring, schedule (edge cases) | new | §P.5 |
| Integration on real SQL: repositories, authorization, double disbursement, limit race | change / new | Testcontainers, existing harness |

## P.2 Changes to the Phase 1 code

### P.2.1 Roles and permission catalog
Mechanism unchanged: permissions in `RolePermissions`, editable in Admin › Roles at runtime, `[HasPermission]`,
cached `UserAccessProfile` with explicit invalidation, `asp-permission`, permission-filtered menu, fail closed,
admin continuity guard, no role-name checks.

**Removed (LAMS-only, no dead code left):** `Segment` + `DataScope.Segment`, `DataScope.Own` / `Assigned` (no
CreditFlow role uses them), `AuthorityLevel` (user attribute, rules, UI, claims-free cache entry), `AuthorityLimit`
aggregate, `LimitType`, `CreditDecisionAuthority`, `PricingExceptionAuthority`, `IAuthorityLimitEvaluator`,
`IPricedResource`, `ICreditDecisionResource`, Admin › Authority limits, placeholders CreditPolicy / Pricing /
Packages, the old permission codes and `DashboardKind` values. `AttributeRule` stays for the branch rule only (Operator:
Required; others: NotAllowed). Columns dropped by a migration (`StaffMembers.Segment`, `.AuthorityLevel`,
`AspNetRoles.SegmentRule`, `.AuthorityLevelRule`, table `AuthorityLimits`).

**New role class** (data on the role, replaces the bool `IsAdministrative`): `RoleKind` = `Business` |
`ReadOnly` | `Administrative`. `PermissionSet` enforces it like the existing SoD rule: Administrative → only `admin.*`;
Business → no `admin.*`; ReadOnly → only permissions the catalog marks `IsReadOnly` (so the Auditor cannot be given a
write permission even by an administrator — fail closed). Each permission in `PermissionCatalog` gets `IsReadOnly`.

**Permission catalog** (R = read-only):

| Module | Code | Meaning |
|---|---|---|
| Clients | `clients.view` (R) | view clients |
| | `clients.manage` | register/edit client, ID document, incomes, obligations |
| Applications | `applications.view` (R) | view applications (within data scope) |
| | `applications.create` | create/edit drafts, parties, collateral, submit |
| | `applications.cancel` | cancel before disbursement (reason) |
| | `applications.record-client-response` | client accepts/declines a counter-offer |
| Documents | `documents.view` (R) / `documents.upload` | view/download; upload, remove before scoring |
| Compliance | `compliance.review` | clear / reject in ComplianceReview |
| Underwriting | `underwriting.decide` | decide up to the Underwriter threshold |
| | `underwriting.decide-senior` | decide up to the Senior Underwriter threshold (incl. escalated files) |
| | `underwriting.counter-offer` | propose new amount/term/rate |
| | `scoring.override` | approve despite an overridable failed rule (reason) |
| Committee | `committee.vote` | vote on files above the Senior threshold |
| Contracts | `contracts.manage` | mark contract signed, mark conditions met, print contract/schedule |
| | `loans.disburse` | request disbursement |
| Loans | `loans.view` (R) / `loans.record-repayment` | view loans; record regular/early repayments |
| Reports | `reports.view` (R) / `reports.export` (R) | reports; Excel/CSV |
| Audit | `audit.view` (R) | audit log (business read-only access for the Auditor) |
| Admin | `admin.users.view`, `admin.users.manage`, `admin.roles.manage`, `admin.branches.manage`, `admin.audit.view`, `admin.products.manage`, `admin.scoring-rules.manage`, `admin.approval-thresholds.manage`, `admin.fx-rates.manage`, `admin.holidays.manage` | |

**Role matrix** (seed; editable afterwards):

| Role (code) | Kind | Data scope | Dashboard | Permissions |
|---|---|---|---|---|
| Operator (`OP`), branch required | Business | Branch | Operator | clients.*, applications.view/create/cancel/record-client-response, documents.*, contracts.manage, loans.disburse, loans.view, loans.record-repayment, reports.view |
| Underwriter (`UW`) | Business | Organization | Underwriting | clients.view, applications.view, documents.view, underwriting.decide, underwriting.counter-offer, loans.view, reports.view |
| Senior Underwriter (`SUW`) | Business | Organization | Underwriting | UW + underwriting.decide-senior, scoring.override, reports.export |
| Credit Committee (`CC`) | Business | Organization | Committee | clients.view, applications.view, documents.view, committee.vote, loans.view, reports.view |
| Compliance (`COMP`) | Business | Organization | Compliance | clients.view, applications.view, documents.view, compliance.review |
| Auditor (`AUD`) | ReadOnly | Organization | Audit | every (R) permission: clients.view, applications.view, documents.view, loans.view, reports.view, reports.export, audit.view |
| Admin (`SYSADMIN`, shown as "Admin") | Administrative | None | Administration | all admin.* |

`DashboardKind` = Operator, Underwriting, Committee, Compliance, Audit, Administration. The Admin role keeps its code
`SYSADMIN` (role codes are immutable and the seeded account and continuity guard reference it); its display name
becomes "Admin".

**Seeding:** unit `0004_creditflow_roles` (new): redefines the kept records (`UW`, `SYSADMIN`), creates
OP/SUW/CC/COMP/AUD with the matrix, moves users of `LO` to `OP`, deactivates users of the other LAMS roles and moves
them to `AUD` (fail closed), deletes the LAMS roles. Units `0001`/`0003` reference types that no longer exist, so they
become **retired** units (id kept in `SeedUnits.All`, `ApplyAsync` does nothing, a comment points to `0004`): on an
existing database they already ran; on a fresh one `0004` produces the same end state. README's seeding rules gain one
line: "a unit may be retired only when a later unit replaces everything it created". `RoleSeedCatalog` describes only
the 7 CreditFlow roles.

### P.2.2 Repository interfaces → Domain (CQRS-lite)
- **Write side (Domain):** one interface per aggregate root next to the aggregate, returning aggregates only:
  `IStaffRepository` (Users), `IRoleRepository` (Roles: profile + `PermissionSet`), `IBranchRepository`,
  `IClientRepository`, `ILoanProductRepository`, `ILoanApplicationRepository`, `IDisbursementRepository`,
  `ILoanRepository`, `IScoringRuleSetRepository`, `IApprovalThresholdsRepository`, `IFxRateRepository`,
  `IHolidayRepository`. Implementations in `Infrastructure/Persistence/Repositories` with EF Core.
- **Read side (Application):** query services returning read models (`IUserDirectory` exists; `RoleDetails` /
  `BranchSummary` move out of the repositories into `IRoleQueries` / `IBranchQueries`; new `IApplicationQueries`,
  `IInboxQueries`, `IClientQueries`, `ILoanQueries`, `IReportQueries`, `IAuditLogQueries` exists). Implemented in
  Infrastructure with `AsNoTracking` projections.
- **Ports that are not repositories** stay in Application: `IUnitOfWork`, `IOutbox`, `IContractNumberGenerator`,
  `IExposureLock`, the external-service interfaces, `IDocumentStore`.
- New architecture tests: every `I*Repository` type lives in `Lams.Domain` and its implementation in Infrastructure;
  no `I*Repository` in Application; EF Core is referenced by no type outside Infrastructure (Domain, Application **and**
  Web); controllers do not touch `DbContext` (exists).

### P.2.3 Small known issues
`RoleId.New()` used by the seed instead of `Guid.CreateVersion7()` (same for `AccountSeeder` via `UserId.New()`); the
unused `Branch.Create(BranchId, …)` overload removed; `.editorconfig` naming rules for private constants/static
readonly (already in the working tree).

**Known gap — antiforgery crawl coverage.** `AntiforgeryFormTests` browses the demo data as every role and checks
each rendered POST form for its token, but the demo data has no record in the state that renders these forms, so
they are allow-listed rather than crawled: MFA management (Disable / RecoveryCodes / Reset — needs a user with MFA
on), Activate for an inactive branch, product or user, Unlock for a locked user, RemoveCollateral / RemoveParty
(needs a draft with collateral or a co-borrower/guarantor), RemoveObligation (needs a client with an obligation),
RepayEarly (rendered only after an early-repayment quote, a GET form the crawl does not submit). Closing it means
creating those states before the crawl.

### P.2.4 Resource-based rules and SQL-side filtering
- `ApplicationAccessPolicy` (Application, pure) turns a `UserAccessProfile` into an `ApplicationAccessFilter`
  (scope: none / branch X / all; `ExcludeCreatedBy`). Infrastructure translates it into a `Where` on the
  `IQueryable` (`WhereVisibleTo(filter)`) used by **every** list, inbox, report and export query — the database
  returns only visible rows; paging counts are correct.
- Inboxes add their own SQL predicates: Underwriting = status Underwriting, `CreatedBy <> me`, not already decided by
  me, tier ≤ my authority; Committee = status CreditCommittee, not created/voted by me; Compliance = ComplianceReview.
- **By id:** every read/write endpoint loads through the same filter (`GetVisibleAsync(id, filter)`); not visible →
  **404** (does not reveal that the id exists). Decisions re-check in the domain (creator / already decided / tier).
- `DataScopeEvaluator` (in-memory) stays for single loaded objects (documents, loans), with the same rules.
- Auditor: read-only by RoleKind (above) **and** an integration test that sends a POST to every non-anonymous POST
  action (found by reflection) as the Auditor and expects 403/404, never a change.

### P.2.5 Logging and masking
- `Serilog.Sinks.MSSqlServer` (Apache-2.0) → table `Logs` created by the EF migration (not by the sink):
  `TimeStamp`, `Level`, `Message`, `Exception`, `UserName`, `RequestPath`, `CorrelationId`, `Properties`.
  User and path come from an enricher (`LogContext` in a middleware after authentication). Console sink stays.
- Masking: `SensitiveDataMaskingSink` decorator (wraps every sink) rewrites each `LogEvent` before it is written:
  properties named like `PersonalNumber`, `Password`, `Token`, `DocumentNumber`, … are masked, and any 11-digit run in
  string values, the rendered message and the exception text becomes `*******1234`. One `SensitiveDataMasker` is
  shared with the audit writer.
- Audit JSON: the existing redaction (password hash, stamps, TOTP) is extended with the masker for client data.
- Audit coverage: application status transitions (one record per history entry), scoring run (per-rule results),
  decisions/votes/overrides, counter-offers, compliance decisions, contract signed, disbursement (requested, succeeded,
  failed), repayments, product/rule-set/threshold/FX/holiday changes, roles/permissions (exists). Written in the same
  transaction as the change.
- Integration test: a log call with a personal number and a password lands in `Logs` masked; an audit record of a
  client change is masked.

### P.2.6 Product name — recommendation
**Recommended: full mechanical rename** `Lams.*` → `CreditFlow.*` (projects, namespaces, solution, cookie/DB names,
UI title) in **one commit before any new code**, so everything new is written once under the final name. It is a
`git mv` + search-and-replace, verified by build and the full test suite; migrations keep their ids. Alternative
(cheaper, less coherent): only the UI title and README. Your choice; default = full rename.

### P.2.7 Architecture choice (README)
Layered Clean Architecture with feature folders, not a modular monolith: one bounded context (loan origination) where
client, application, disbursement and loan share transactions; one developer and 2–3 days; module contracts would add
ceremony without isolation benefits. Layer rules are enforced by NetArchTest.

### P.2.8 How existing tests are kept
- Ported: user administration, account/MFA/session, roles editor, branches, audit log, menu, seeding, web shell — with
  the new roles and personas (expected matrices rewritten for 7 roles from an independent table, as in Phase 1).
- Removed **with the removed feature only**: pricing-exception, authority-limit (domain, evaluator, admin screen),
  segment / authority-level rule tests. Their replacements: approval-thresholds domain/admin tests.
- Every step ends with the full suite green; the count is recorded in the progress table.

## P.3 Domain design

### P.3.1 Aggregates and boundaries
| Aggregate | Contents | Why separate |
|---|---|---|
| `Client` | `PersonalNumber` (11 digits, unique), names, birth date, contact, `IdentityDocument` (type, number, issued, expires), incomes (amount, currency, source), declared obligations, KYC/screening status (last sanctions/PEP/KYC result + date) | lives across many applications and loans |
| `LoanProduct` | type (Consumer/Mortgage/Auto), versions: min/max amount, term, rate range, upfront fee %, monthly fee, early-repayment penalty %, collateral required + max LTV, document checklist rules (doc type, min amount), schedule types allowed, currencies; one active version | versioned reference data edited by Admin |
| `LoanApplication` | parties (borrower + co-borrowers + guarantors, `ClientId` each), requested terms, **product snapshot** (owned), FX rate used, collateral items, document metadata, status + `StatusHistory`, scoring runs + rule results, decisions (incl. votes, overrides), counter-offers, conditions, offer expiry, escalation, contract number/signing, branch, created by, `RowVersion` | the consistency boundary of the whole process: every transition and invariant in one place, protected by one rowversion |
| `Disbursement` | `ApplicationId` (unique), `IdempotencyKey` (unique), status Pending/Succeeded/Failed, core banking tx id, attempts | must be committed **before** the external call, independently of the application |
| `Loan` | contract number, principal, rate, schedule type, installments, repayments, outstanding principal, Active/Closed, `RowVersion` | after disbursement; repayments do not lock the application |
| `ScoringRuleSet` | versions; each version: rules (code, parameters JSON, overridable, order); one active version | versioned rules; applications reference the version used |
| `ApprovalThresholds` | UW max, Senior UW max (in GEL), committee votes required (2), committee size (3) | configurable by Admin, audited |
| `FxRate`, `Holiday` | (currency, date, rate to GEL); (date, name) | reference data |

Pure domain services (unit-tested, no I/O): `ScoringEngine` + one `IScoringRule` per rule code, `ApprovalAuthority`,
`ScheduleCalculator` (annuity, differentiated, EIR), `EarlyRepaymentCalculator`, `WorkingDayCalendar`,
`ExposureCalculator`, `Money` (rounding), `PersonalNumber`, `AgeRules`.

### P.3.2 Status transitions (everything else is impossible)
| From | To | Trigger (who) | Guards |
|---|---|---|---|
| Draft | Submitted | submit (Operator) | parties valid, borrower ≥18, amount/term/rate within active product version, collateral present if required, ID document valid, no other open application of the borrower for the product; product snapshot + FX rate stored |
| Submitted | PreCheck | system (outbox) | — |
| PreCheck | ComplianceReview | system | sanctions or PEP match on any party |
| PreCheck | Rejected | system | KYC / ID verification failed for any party |
| PreCheck | DocumentsPending | system | all parties clear, bureau reports received (unavailable → stays in PreCheck, retried) |
| ComplianceReview | DocumentsPending / Rejected | compliance (reason) | decider ≠ creator, one decision per person |
| DocumentsPending | Scoring | Operator "send to scoring" | checklist for product + amount complete |
| Scoring | Rejected | system | a non-overridable rule failed |
| Scoring | Underwriting | system | GEL amount ≤ Senior UW threshold; tier stored (UW / Senior) |
| Scoring | CreditCommittee | system | GEL amount > Senior UW threshold |
| Underwriting | Approved / ConditionallyApproved | decide (UW/Senior) | authority tier, not creator, not decided before, overridable failures need `scoring.override` + reason |
| Underwriting | Rejected | decide (reason) | same |
| Underwriting | CounterOffered | counter-offer | new terms within the snapshot ranges |
| CreditCommittee | Approved / ConditionallyApproved / Rejected | 2nd matching vote | member ≠ creator, one vote per member |
| CounterOffered | Scoring | client accepts (Operator) | terms replaced, scoring rerun |
| CounterOffered | Cancelled | client declines (Operator, reason) | — |
| CounterOffered, Approved, ConditionallyApproved | Expired | job | 30 days passed |
| Approved, ConditionallyApproved | ContractSigned | Operator | offer valid; contract number assigned |
| ContractSigned | ConditionsMet | Operator marks last condition met (or automatically when there are none) | all conditions met |
| ConditionsMet | Disbursed | disbursement succeeded | ID documents valid today, exposure re-checked under lock |
| Disbursed | Active | system (outbox: book loan + schedule) | — |
| Active | Closed | repayment brings outstanding to 0 | — |
| any of Draft … ConditionsMet | Cancelled | Operator (reason) | no disbursement requested |

Every transition appends `StatusHistory(from, to, at, by, reason)` and raises a domain event (audit + outbox
notification). Escalation is a flag on the application (`EscalatedAt`), not a status: after it the file needs
`underwriting.decide-senior`.

### P.3.3 Database outline (new tables; ids are GUID v7 unless noted)
- `Clients` (PersonalNumber unique, …, RowVersion), `ClientIncomes`, `ClientObligations`
- `LoanProducts`, `LoanProductVersions` (ProductId, VersionNo unique, IsActive, terms, `ChecklistJson`)
- `LoanApplications` (Status, BranchId, CreatedBy, BorrowerClientId, ProductId, `IsOpen` bit, Amount, Currency,
  AmountGel, ApprovalTier, StatusChangedAt, OfferExpiresAt, EscalatedAt, ContractNumber unique-filtered,
  snapshot columns (owned), RowVersion);
  `ApplicationParties`, `ApplicationCollaterals`, `ApplicationConditions`, `ApplicationStatusHistory`,
  `ApplicationDecisions` (Kind, DecidedBy, Reason; **unique (ApplicationId, DecidedBy)**), `CounterOffers`,
  `ScoringRuns`, `ScoringRuleResults`, `ApplicationDocuments` (metadata) + `DocumentContents` (varbinary, separate
  so lists never load blobs)
- `Disbursements` (ApplicationId unique, IdempotencyKey unique, Status, CoreBankingTransactionId)
- `Loans`, `LoanInstallments`, `LoanRepayments`
- `ScoringRuleSets`, `ScoringRuleSetVersions`, `ScoringRules`; `ApprovalThresholds`; `FxRates` (Currency, Date) PK;
  `Holidays` (Date PK)
- `ContractNumberSequences` (Year PK, LastNumber) — locked row, not a SEQUENCE
- `OutboxMessages` (Type, Payload, OccurredAt, AvailableAt, Attempts, ProcessedAt, LastError),
  `ProcessedMessages` (MessageId, Handler) PK — handler idempotency
- `Notifications`; `Logs`; mock tables `SanctionsListEntries`, `MockCoreBankingTransactions`
- Indexes: one open application `UX (BorrowerClientId, ProductId) WHERE IsOpen = 1`; operator list
  `(BranchId, Status, StatusChangedAt)`; inboxes `(Status, ApprovalTier, StatusChangedAt) INCLUDE (CreatedBy, AmountGel)`;
  jobs `(Status, OfferExpiresAt)`, `(Status, EscalatedAt, StatusChangedAt)`; outbox `(ProcessedAt, AvailableAt)`
  filtered on unprocessed; reports `ApplicationStatusHistory (ToStatus, ChangedAt)`, `Loans (ProductId, Status)`.

### P.3.4 Scoring rules (version 1, seeded; parameters editable as a new version)
`BUREAU_DPD30_12M`
(hard), `PTI` (bands, FX stress, **overridable**), `LTV` (per product type, hard), `VALUATION_AGE` (≤6 months, hard),
`FX_INCOME_CURRENCY` (below threshold, hard), `EXPOSURE_LIMIT` (hard), `PRODUCT_LIMITS` (hard). The DB stores which
rules are active, their parameters, severity and version; the logic of each rule code is code (`IScoringRule`), so a
rule set version is fully reproducible. Each run stores rule set version, every rule's pass/fail, measured value,
threshold and a human reason ("PTI 52.3% > 45% for income 1,000–3,000 GEL").

### P.3.5 Concurrency and idempotency
- **rowversion** on every aggregate root; `DbUpdateConcurrencyException` → `Result` error `Concurrency.Conflict`.
- **Two underwriters approve at once:** both load version V; the second save fails on rowversion → reloaded, the
  status is no longer Underwriting → "already decided". Proven by an integration test with two scopes.
- **Concurrent committee votes:** votes are commutative, so the vote command retries **once** on a rowversion
  conflict after reloading; both votes survive, and the second matching vote decides. Unique (ApplicationId,
  DecidedBy) stops double votes even under retries. Test: two/three parallel votes.
- **Gapless contract numbers:** inside the signing transaction `UPDATE ContractNumberSequences WITH (UPDLOCK,
  HOLDLOCK) SET LastNumber = LastNumber + 1 OUTPUT inserted.LastNumber WHERE Year = @year` (row inserted if missing),
  so a rollback returns the number; unique index on `ContractNumber`. Year = Tbilisi local year.
- **Idempotent disbursement:** idempotency key = deterministic per application (`disb:{applicationId}`), so a double
  click, a retry or a second browser cannot create two payments. Step 1 (transaction): guards, `sp_getapplock`
  per party (`exposure:{clientId}`, sorted to avoid deadlocks), exposure re-check, insert `Disbursement(Pending)`
  (unique index → the second request finds the existing row and returns it). Step 2: call core banking with the key
  (the mock stores keys, so a retried key returns the same tx id). Step 3: success → `Disbursed` + outbox "book
  loan"; error → `Failed`, retry allowed with the same key; timeout → stays Pending, retried by the outbox with the
  same key. Pending exposure counts in the exposure total, so two parallel disbursements that together exceed the
  limit cannot both pass. Tests: double request, parallel requests, timeout + retry, two clients' loans in parallel
  over a shared guarantor.

## P.4 Ambiguities and decisions
1. **ConditionallyApproved vs Approved** — the decider may attach conditions (e.g. insurance, extra document);
   with any → ConditionallyApproved, none → Approved. Product-standard conditions (mortgage/auto: collateral
   registration + insurance) are added to every approval of that product. Both go through ContractSigned →
   ConditionsMet; with no conditions ConditionsMet follows signing automatically (both recorded).
2. **Disbursed vs Active** — Disbursed = core banking confirmed the transfer (persisted immediately); Active = loan
   and schedule booked by the outbox handler (retryable, idempotent). Closed = outstanding principal 0.
3. **Client consent / offer validity** — the offer is accepted by signing the contract within 30 calendar days of the
   decision; a counter-offer must be accepted within 30 days too, else Expired. Declined counter-offer → Cancelled.
4. **Co-borrowers and guarantors** — every party: KYC, sanctions/PEP, bureau, age rules, DPD rule. PTI = household:
   borrower + co-borrowers' incomes vs their obligations + new installment. Guarantor: own PTI including the full new
   installment must pass. Exposure: borrower and co-borrowers count the full amount, guarantors the guaranteed amount.
5. **PTI thresholds** (net monthly income in GEL equivalent): < 1,000 → 35%; 1,000–3,000 → 45%; ≥ 3,000 → 55%.
   Obligations = bureau (other banks) + our active loans + operator-declared. Configurable in the rule set.
6. **FX stress** — for USD/EUR loans the new installment (converted to GEL) is multiplied by 1.20 in PTI,
   regardless of income currency (task wording).
7. **FX threshold** — a USD/EUR loan whose GEL equivalent is below 200,000 GEL requires income in the loan currency
   (in line with NBG practice); configurable.
8. **Exposure limit** — 300,000 GEL equivalent per client (outstanding principal of our active loans as borrower or
   co-borrower + guarantees + approved/pending disbursements + the new loan); configurable.
9. **Thresholds currency** — approval thresholds and limits are in GEL; foreign amounts converted at the latest
   `FxRates` rate on or before the scoring date, stored on the scoring run (routing does not drift with later rates).
10. **Rule failure vs override** — non-overridable failure → automatic Rejected; overridable (PTI only, seeded) → the
    file continues to review flagged, and approval needs `scoring.override` + a reason (audited).
11. **Four-eyes scope** — "one decision per person per application" over the whole life of the application
    (compliance decision, underwriting decision, counter-offer, vote). After a counter-offer, a different underwriter
    decides. The creator never decides. DB-enforced by the unique index.
12. **Committee** — members = active users with `committee.vote` (3 seeded). Votes Approve (optionally with conditions)
    or Reject (reason). 2 matching votes decide; conditions = union of approving votes. No counter-offer by the
    committee (only by underwriters), documented.
13. **Escalation** — deadline = entry into Underwriting + 2 working days (Asia/Tbilisi, weekends and `Holidays`
    excluded); after it the job sets `EscalatedAt`, notifies, and only `underwriting.decide-senior` can decide.
    Senior UW can always decide UW-tier files too.
14. **Rounding** — `decimal` only; one helper `Money.Round` = 2 decimals, `MidpointRounding.AwayFromZero`; interest
    per period computed exactly and rounded once per installment; the last installment absorbs the difference so the
    principal sum equals the amount exactly. Rates as `decimal` percentages.
15. **Schedule conventions** — monthly installments, first due one month after disbursement, interest = annual rate/12
    per period. EIR = monthly IRR (decimal Newton/bisection on integer periods) of the cash flows (amount − upfront fee,
    installments + monthly fees), annualised `(1+r)^12 − 1`.
16. **Early repayment** — partial: keeps the remaining term and lowers the installment (annuity recalculated /
    differentiated principal re-spread); full: outstanding principal + interest accrued since the last due date
    (actual/365) → Closed. Penalty = product % of the prepaid principal (seed: consumer 2%, auto 1%, mortgage 1%).
17. **Repayments** — there is no core banking for collections: an Operator records repayments manually
    (`loans.record-repayment`), applied to the oldest unpaid installment; seed data includes paid installments.
18. **Document storage** — DB (`DocumentContents`, varbinary), so it is in the same transaction and backup; PDF, JPEG,
    PNG only, checked by magic bytes, ≤ 10 MB, SHA-256 stored, served with `Content-Disposition: attachment`.
19. **Bureau unavailable** — the PreCheck outbox message is retried with exponential backoff (1, 2, 4 … min, cap 1 h);
    the application stays in PreCheck, never lost; after 10 attempts it is flagged for the operator (still retried).
    "Bureau retry job" = the outbox dispatcher with this backoff policy (no separate job).
20. **Open application** — open = Draft … ConditionsMet. Disbursed/Active are loans, not open applications.
21. **Age at last installment** — at submission, maturity = submission date + term; re-checked with the real schedule
    at disbursement.
22. **Cancel** — allowed Draft … ConditionsMet with a reason, but not once a disbursement is Pending (money may be in
    flight).
23. **Operator scope** — Operators see all applications of their branch (not only their own); the branch is fixed at
    creation from the creator's branch.
24. **Out-of-scope by id** → 404, not 403 (no existence leak).
25. **Who disburses** — the Operator requests it; the system executes it (mock core banking).
26. **Notification recipients** — status changes → the creating operator and the client (SMS/email rows);
    escalation → active Senior Underwriters; offer expiry → operator + client.

Decisions taken during implementation:

27. **Product amount limits are in GEL** — a USD/EUR loan is checked by its GEL equivalent at the submission-date
    rate (same convention as the approval thresholds).
28. **Fees** — upfront fee as % of the principal (deducted at disbursement, counts in the EIR); monthly service fee as %
    of the original principal per month (currency-neutral, so one product version serves GEL, USD and EUR).
29. **Regulatory LTV ceilings are code** (mortgage 80 %, auto 70 %); a product version may be stricter, never looser.
30. **Optimistic concurrency** — `rowversion` is an EF Core shadow property on each aggregate root (mapped in P8), so
    the domain model stays free of persistence details.
31. **Bank time** — business dates (age, document validity, holidays, working days, contract year) are dates in
    Asia/Tbilisi (UTC+4); instants are stored in UTC.
32. **Age rules are fixed policy, not scoring parameters** — at least 18 at application and at most 75 at the last
    installment are checked for every party at submission (all errors at once) and again at disbursement; they are not
    in the scoring rule set (`AGE_MIN` / `AGE_MAX_AT_MATURITY` dropped from §P.3.4).
33. **Committee approvals** carry the product's standard conditions only; a committee vote is Approve or Reject
    (reason required), refining #12.
34. **Counter-offer** keeps currency and schedule type; amount, term and rate must fit the snapshot. Accepting it needs
    the checklist for the new amount (documents may be added while the offer is pending).
35. **Scoring rules: data vs. code** — the DB holds which rules a version uses, their parameters (JSON) and whether a
    failure is overridable; the logic of each rule code is code (`IScoringRule`). Comparisons use exact values; measured
    values are only rounded for display (a found bug: rounding first let 45.004 % pass a 45 % PTI limit).
36. **EIR** — monthly IRR of the client's cash flows (principal − upfront fee received; installments + monthly fees
    paid), annualised (1 + i)^12 − 1, solved by bisection in `decimal`.
37. **Aggregate-wide concurrency** — besides the `rowversion`, every root has a `Revision` counter that the consistency
    interceptor increments whenever a cascade-owned part changes, so a change that only adds child rows (a committee vote,
    a document) still updates and checks the root: two concurrent changes to one aggregate cannot both succeed.
38. **Persistence of value objects** — required value objects with plain columns are EF complex types (identity
    document, loan terms, money); optional or list-shaped ones are JSON columns (product terms, snapshot, exchange rate,
    counter-offered terms, scoring runs, rule definitions), read and written as a whole and never queried in SQL.
39. **Outbox** — events become `OutboxMessages` in the same SaveChanges (interceptor). The dispatcher claims due
    messages in a short transaction (`UPDLOCK, READPAST`, claim expiry 5 min) and runs every handler in its own
    transaction that also inserts `ProcessedMessages (MessageId, Handler)`, so retries never repeat a handler. Failures
    retry with backoff 1, 2, 4 … minutes (max 1 h); the bureau retry of decision #19 is this backoff.
40. **Mocks** — scenarios by personal number (seeded catalog, overridable in `Mocks:Scenarios`); timeouts are real
    (a hanging call cancelled after `Mocks:BureauTimeout`). Notifications are rows in `Notifications` plus a log line.
41. **Exposure at disbursement counts committed money only** — the re-check under the per-party `sp_getapplock` adds
    payouts that are Pending or done and the outstanding principal of active loans (in GEL at each application's
    scoring rate) to the new loan; approvals that were never paid out do not block a payout (scoring still counts them,
    conservatively). The limit is the `EXPOSURE_LIMIT` of the rule-set version the application was scored with.
42. **Payout amount** — the principal less the upfront fee; the schedule runs on the full principal from the
    disbursement day.
43. **Core banking mock** — its ledger (`MockCoreBankingTransactions`, key = idempotency key) is written in its own
    scope, like a separate system: 01001011009 rejects the first request of a key; 01001011010 executes the first
    request but answers after `Mocks:CoreBankingTimeout`, so the retry with the same key returns the same transaction.
44. **Repayments** — "next installment paid" records the oldest unpaid installment in full; an early repayment is
    refused while an installment is overdue (record it first).
45. **Jobs** — escalation and offer expiry run every `Jobs:Interval` (1 min) in one `BackgroundService`; each batch
    claims candidates with `UPDLOCK, READPAST, ROWLOCK` in its transaction (keyset by id, so candidates that are not
    due yet are not revisited in the same run) and commits the domain change with them. Running twice or in parallel
    gives one effect. Escalation notifies the active users holding `underwriting.decide-senior`; expiry notifies through
    the status-change notifications (operator and client).
46. **PDFs** — PDFsharp-MigraDoc 6.2.4 (MIT) with DejaVu Sans embedded in the Infrastructure assembly (Bitstream Vera
    licence; Latin and Georgian glyphs; the same output on every OS, no system fonts). The contract can be printed from
    the approval on (the number reads "assigned at signing" until then); the schedule is projected from today before
    disbursement and is the loan's current schedule afterwards. Printing needs `contracts.manage` or `loans.view`.
47. **Wizard** — step 1 (borrower, product, terms) creates the draft; steps 2–4 (parties and collateral, documents,
    review and submit) edit that draft through the normal actions, so the wizard adds no second write path. A draft's
    document checklist comes from the product's current version; from submission on, the snapshot rules.
48. **Payout on the page** — Pending shows "check again" and Failed shows "retry"; both post the same Disburse action,
    which reuses the payout and its idempotency key.
49. **Reference-data admin** — product terms and scoring rule sets are append-only versions (the newest is in force,
    nothing is edited in place, an unchanged form publishes nothing); approval thresholds, FX rates and holidays are
    edited in place with before/after in the data audit. An FX CSV import is all or nothing and names every wrong line.
    The admin services re-check their `admin.*` permission behind the controller attribute.
50. **Reports** — eight reports (applications by status, approval / rejection rate, average time per stage, rejection
    reasons, portfolio by product, disbursed volume by product and branch, overdue installments, escalations) for a
    period (default the last 90 days, at most 3 years) and optionally a product, always within the viewer's data scope
    in SQL. Period semantics: applications created, decisions taken, stages left, loans disbursed, escalations in the
    period; the portfolio and overdue installments are as of today. Amounts are GEL at each application's scoring rate.
    A scoring rejection counts once per failed hard rule. One `ReportTable` feeds the page, the CSV (one report, UTF-8
    with BOM, formula injection neutralised) and the Excel workbook (ClosedXML 0.105, MIT; its dependencies are MIT /
    Apache-2.0, SixLabors.Fonts 1.0 is pre-split-licence). Export needs `reports.export` (not held by Operators).
51. **Demo data** — Development only, behind `Seed:DemoData:Enabled` (on in `appsettings.Development.json`, off in the
    tests that do not test it), once per database (`SeedHistory`), in one transaction, after the demo users. 24
    applications built through the domain with backdated times; five are submitted and left to the live pre-check.
    Submitted, Scoring and Disbursed are passing states (seconds) and are only seen in histories. A mock-scenario
    client that already has an open application (a scenario tried by hand earlier) is skipped, not duplicated.
Decisions taken after Phase 2 (fixes from the first run of the demo data):

52. **Application reference** — `APP-000123` from a database sequence (`ApplicationNumbers`), assigned on the first
    save, unique, searchable. The former reference (the first 8 hex digits of the time-ordered id) was the timestamp
    and repeated for applications created within about a minute. Existing applications are numbered in creation order
    by the migration. The audit log abbreviates ids by their random end.
53. **Contact data in logs and audit** — email addresses keep the first character and the domain; phone numbers keep
    their last two digits; phone, email and recipient fields are always masked.
54. **Query warnings** — `Database:ThrowOnQueryWarnings` (Development and tests) turns EF Core's
    multiple-collection-include warning into an exception, so such a query cannot reach production unnoticed.

## P.5 Tests (added on top of the ported suite)
- **Unit (Domain):** every allowed transition and every forbidden pair (table-driven over all 19×19), history entries,
  cancel rules, four-eyes; each scoring rule at its boundaries (17y364d / 18, 75 at maturity, PTI band edges and FX
  stress, LTV 80/80.01, valuation 6 months ±1 day, DPD 29/30, exposure = limit / +0.01, FX threshold); approval tier
  boundaries (20,000 / 20,000.01, 100,000 / 100,000.01); schedule (annuity / differentiated, 1-month term, 0% rate,
  rounding to the last installment, principal sum = amount, EIR known values), early repayment (partial, full,
  penalty); working-day calendar (weekends, holidays, Friday → Tuesday); `Money`, `PersonalNumber`.
- **Integration (Testcontainers SQL Server):** every repository round-trip incl. rowversion conflict; SQL-side list
  filtering per role and IDOR by id (operator of another branch, underwriter on own application, auditor POSTs);
  double disbursement (same request twice, parallel); exposure race (two parallel disbursements exceeding the limit
  together → exactly one succeeds); concurrent committee votes (no lost vote) and concurrent approvals (one wins);
  gapless contract numbers under parallel signing and after a rollback; outbox claiming by two dispatchers
  (each message once) and handler idempotency; escalation / expiry jobs run twice → one effect; log and audit masking.
- **Architecture:** layers, repository interfaces in Domain, EF only in Infrastructure, controllers without DbContext.

## P.6 Mocks and scenarios
Interfaces in Application (`ISanctionsScreeningService`, `ICreditBureauClient`, `IIdentityVerificationService`,
`ICoreBankingClient`, `INotificationSender`), mocks in `Infrastructure/ExternalServices`, scenarios in configuration
(`Mocks:Scenarios:{personalNumber}`) plus the DB sanctions list; timeouts are real `CancellationToken` timeouts.
Seeded test personal numbers (also in README):

| Personal number | Scenario |
|---|---|
| 01001011001 | clean: every check passes |
| 01001011002 | sanctions match → ComplianceReview |
| 01001011003 | PEP match → ComplianceReview |
| 01001011004 | KYC: document not verified → Rejected |
| 01001011005 | bureau: 30+ DPD in last 12 months → Rejected at scoring |
| 01001011006 | bureau always unavailable → stays in PreCheck, retried |
| 01001011007 | bureau times out twice, then answers → retry succeeds |
| 01001011008 | bureau: large obligations → PTI fails (overridable) |
| 01001011009 | core banking error → Failed, retry with the same key |
| 01001011010 | core banking timeout (payment done, answer late) → retry returns the same tx id |
| 01001011011 | existing exposure near the limit → exposure scenarios |
| 01001011012 | USD loan with GEL income below the FX threshold → rejected |

## P.7 Steps (each: build 0 warnings, tests green, PLAN progress, one commit, short summary)

Two days: Day 1 = P0–P9, Day 2 = P10–P17. The browser flow works end to end after P11.

**Day 1 — foundation, domain, persistence**
- [x] **P0** Housekeeping: revert stray edits, `.editorconfig` naming rules, `RoleId.New()`/`UserId.New()` in seeding,
  remove unused `Branch.Create` overload; README: decision "EF Core instead of Dapper (agreed with the bank)".
- [x] **P1** Rename `Lams` → `CreditFlow` (mechanical, one commit). Projects, folders, namespaces, solution, `Lams*`
  types, cookies (`__Host-CreditFlow.Auth` / `.Af`), claim / session keys (`creditflow:role_id`, `creditflow.issued`),
  Data Protection application name and TOTP issuer, connection string name `ConnectionStrings:CreditFlow`, design-time
  variable `CREDITFLOW_DESIGN_CONNECTION`, docker container / volume `creditflow-sqlserver` / `creditflow-sqldata`.
  Kept: `UserSecretsId` (local secrets survive), migration ids, `docs/`, and the Phase 1 sections of this file
  (history, old names on purpose).
- [x] **P2** Roles & permissions: remove LAMS-only concepts, `RoleKind`, new catalog/matrix/dashboards, seed unit
  `0004`, migration, dev personas for 7 roles (≥2 UW, ≥2 Senior UW, 3 committee, 1 Compliance, 1 Auditor,
  Operators in 2 branches); ported tests.
- [x] **P3** Repository interfaces to Domain, query interfaces in Application, architecture tests.
- [x] **P4** Logging to SQL + masking (logs and audit) + tests.
- [x] **P5** Domain primitives: `Money`, `Currency`, `PersonalNumber`, FX conversion, `WorkingDayCalendar`; `Client`,
  `LoanProduct` (versions) + unit tests.
- [x] **P6** `LoanApplication` aggregate and state machine + transition tests.
- [x] **P7** Scoring engine + rules, approval authority, schedule/EIR/early-repayment calculators + unit tests.

- [x] **P8** EF mappings, migration, repositories (rowversion), reference seed (products v1/v2, rule set, thresholds,
  FX, holidays, sanctions list) + repository integration tests.
- [x] **P9** Outbox + dispatcher + mocks + PreCheck (bureau retry) + notifications + tests.

**Day 2 — flow in the browser, concurrency, breadth**
- [x] **P10** Application services for intake → scoring → underwriting/committee/compliance/counter-offer, access
  filter + SQL-side lists + by-id checks; integration tests (scope, four-eyes, IDOR, concurrent approve/vote).
  **Minimal pages:** clients (register + KYC data, incomes, obligations), application create/submit + document
  upload, application list and page (status history, scoring result), Underwriting / Committee / Compliance inboxes
  with decide / counter-offer / vote / clear-reject actions.
- [x] **P11** Contract (gapless numbers), disbursement (idempotent, exposure lock), loan booking, repayments; tests
  (double disbursement, exposure race, numbering). **Minimal pages:** client response to a counter-offer, mark
  contract signed, mark conditions met, disburse, loan page with schedule, record repayment. **End-to-end flow works
  in the browser here.**
- [x] **P12** Jobs: escalation, offer expiry (+ idempotency tests).
- [x] **P13** Web polish of the P10/P11 pages: multi-step application wizard, scoring explanation, decisions panel,
  contract + schedule PDF, loans list.
- [x] **P14** Admin: products with versions, scoring rule-set versions, approval thresholds, FX rates (+ CSV import),
  holidays.
- [x] **P15** Reports + Excel/CSV export.
- [x] **P16** Dev data: applications in every status (created through the domain, not raw inserts).
- [x] **P17** README complete (run, architecture, auth, EF Core decision, UoW/outbox/jobs, concurrency, rounding,
  mocks, accounts, ambiguities, licences); final verification.
  Also replace the "(this commit)" placeholders in the Phase 1 history table (§0 Progress) with the real commit hashes.

**Cut first if time runs short** (in this order): FX CSV import (manual edit stays) → scoring-rule-set editing UI
(versions seeded + read-only view) → holiday admin UI (seeded only) → Excel export (CSV stays) → the rename (UI title
only) → differentiated early-repayment re-spread (annuity kept). Never cut: state machine, scoring, four-eyes,
idempotent disbursement, exposure race, rowversion, SQL-side filtering, masking, tests for those.

**Libraries** (licences allow commercial use): PDFsharp-MigraDoc (MIT) for PDFs, ClosedXML (MIT) for Excel,
Serilog.Sinks.MSSqlServer (Apache-2.0); CSV by a small RFC 4180 writer with formula-injection protection. Not
QuestPDF (Community licence excludes companies above $1M revenue) and not EPPlus (commercial).

## P.8 Progress
| Step | Status | Commit |
|---|---|---|
| A — Plan | approved 2026-10-12 | `395c343` |
| P0 — Housekeeping | done; build 0 warnings, 798 tests green (Domain 135, Application 519, Architecture 9, Integration 135) | `5c99de0` |
| P1 — Rename to CreditFlow | done; build 0 warnings, 798 tests green, no pending EF model changes | `efa0d9f` |
| P2 — CreditFlow roles and permissions | done; build 0 warnings, 531 tests green (Domain 95, Application 300, Architecture 9, Integration 127). The drop from 798 is the removed features: authority-limit and pricing-exception tests (domain, evaluator, admin screen) and the smaller permission matrix (31 permissions × 7 roles instead of 39 × 10). New: read-only role kind, LAMS → CreditFlow conversion of an existing database, Auditor audit-log access, dashboards per role. | `9f2042a` |
| P3 — Repositories in Domain, query services in Application | done; build 0 warnings, 535 tests green (Architecture 13: repository interfaces only in Domain and implemented in Infrastructure, query services in Application, EF Core only in Infrastructure) | `beb50a6` |
| P4 — Logs in MS SQL, masking in logs and audit | done; build 0 warnings, 557 tests green (Domain 95, Application 318, Architecture 13, Integration 131; integration run 3.7 min, slowest test 4 s). Found and fixed on the way: request logs went to the static Serilog logger (never written), and SqlClient 6.1 (pulled by the sink) retried the not-yet-created test database for 10 s per test. | `3e4dc87` |
| P5 — Domain primitives, Client, LoanProduct | done; build 0 warnings, 614 tests green (Domain 152, Application 318, Architecture 13, Integration 131) | `a7d9b79` |
| P6 — LoanApplication aggregate and state machine | done; build 0 warnings, 1,036 tests green (Domain 574 incl. all 361 status pairs, Application 318, Architecture 13, Integration 131) | `d35133f` |
| P7 — Scoring, approval thresholds, schedule, EIR, early repayment | done; build 0 warnings, 1,102 tests green (Domain 630, Application 328, Architecture 13, Integration 131) | `621054e` |
| P8 — Persistence of the lending model | done; build 0 warnings, 1,112 tests green (Domain 630, Application 328, Architecture 13, Integration 141), no pending EF model changes | `ba77864` |
| P9 — Outbox, dispatcher, mocks, pre-check, notifications | done; build 0 warnings, 1,122 tests green (Domain 630, Application 328, Architecture 13, Integration 151) | `be84877` |
| P10a — Application services, access filter, decisions | done; build 0 warnings, 1,157 tests green (Domain 630, Application 353, Architecture 13, Integration 161), no pending EF model changes. New: full flow through the services (registration → pre-check → scoring → approval, audited), compliance clearing, counter-offer with a second underwriter, branch scope and IDOR by id, the creator never deciding, tiered inboxes, concurrent approvals and committee votes, Auditor read-only, upload rules. | `24ae829` |
| P10b — Client, application and inbox pages | done; build 0 warnings, 1,163 tests green (Domain 630, Application 353, Architecture 13, Integration 167). Pages: clients (list, register, edit, KYC document, incomes, obligations), applications (list with SQL-side filters / sort / paging, new, application page with parties, collateral, documents, scoring explanation, history, decisions and every action the viewer may take), decision inboxes. Web tests: operator flow through the pages, upload by content, IDOR by id (404), the Auditor's POST to every permission-protected action refused with nothing changed, unprotected POSTs only account self-service. Fixed on the way: decisions with the same timestamp were listed in an undefined order. | `c842907` |
| P11 — Contract, disbursement, loans, repayments | done; build 0 warnings, 1,190 tests green (Domain 646, Application 353, Architecture 13, Integration 178), no pending EF model changes. Gapless contract numbers from a locked counter row in the signing transaction; idempotent payout (deterministic key, Pending row committed before core banking, rejection → retry with the same key, timeout → background retry returns the same transaction); exposure re-check under per-party `sp_getapplock` in id order; loan booking via the outbox; installments and early repayment (partial recalculation, full closes loan and application). Tests: parallel signing + rollback, double payout sequential and parallel, error and timeout, exposure race over a shared guarantor, repayments, loan IDOR, pages. Decisions #41–#44. | `8633e23` |
| P12 — Escalation and offer-expiry jobs | done; build 0 warnings, 1,197 tests green (Domain 646, Application 353, Architecture 13, Integration 185), no pending EF model changes. One `BackgroundService` (`Jobs:Enabled`, `Jobs:Interval`) runs both jobs; candidates claimed with `UPDLOCK, READPAST, ROWLOCK` per batch (keyset by id); escalation after 2 working days (weekends and holidays skipped) notifies the Senior Underwriters; expiry after 30 days notifies operator and client. Tests: deadline across a weekend and a holiday, run twice and two parallel runners → one effect each, inbox and authority after escalation, notifications. Index for the escalation claim added. Seen once: `LoggingTests.A_log_event_reaches_the_database…` timed out (15 s poll for the batched SQL sink) during a full run under load; it passed alone and in the next full run. | `0aa641f` |
| P13 — Pages: wizard, application page, contract and payout, loans, PDFs | done; build 0 warnings, 1,206 tests green (Domain 646, Application 353, Architecture 13, Integration 194), no pending EF model changes. Application wizard (borrower and terms → parties and collateral → documents → review and submit) on the normal actions; application page split into cards (scoring verdict and per-rule explanation, history, decisions with committee tally, conditions, counter-offer, contract and payout with Pending / Failed states and check-again / retry); loans list (SQL-side filter and paging, scope); client page lists the client's applications; contract and schedule PDFs (PDFsharp-MigraDoc 6.2.4, embedded DejaVu Sans). Web tests walk the whole process role by role (operator, underwriter, compliance, two committee members, auditor), payout rejection and timeout, PDF permissions and IDOR. `LoggingTests` now polls the batched SQL sink against a 60 s deadline (assertions unchanged). Decisions #46–#48. | `fe583a9` |
| P14 — Admin: products, scoring rules, thresholds, FX rates, holidays | done; build 0 warnings, 1,233 tests green (Domain 646, Application 364, Architecture 13, Integration 210), no pending EF model changes. Admin › Products (create with v1, publish the next version, rename, (de)activate), Scoring rules (versions, publish the next version with edited parameters / overridable flags, validated by the engine), Approval thresholds, FX rates (set, delete, CSV import all or nothing), Holidays (add, rename, remove). Services re-check `admin.*`; every change is in the data audit. Tests: per area incl. snapshot unaffected by a new product version, invalid input changes nothing, business roles get AccessDenied; pages render, forms and CSV upload round-trip; the Auditor POST sweep covers the new actions. Decision #49. | `4156d51` |
| P15 — Reports and Excel / CSV export | done; build 0 warnings, 1,239 tests green (Domain 646, Application 364, Architecture 13, Integration 216), no pending EF model changes. Eight reports (applications by status, approval / rejection rate, average time per stage with `LEAD` in parameterised SQL, rejection reasons, portfolio by product, disbursed volume by product and branch, overdue installments, escalations), period and product filters, the viewer's data scope in SQL; Excel (ClosedXML, every report a sheet) and CSV (one report, injection-safe). The placeholder "coming soon" page is gone. Tests: figures on a two-branch data set, operator = branch / auditor = bank on the page model and in both exports, CSV escaping, permissions, page and downloads over HTTP. Decision #50. | `d7b84d7` |
| P16 — Development demo data | done; build 0 warnings, 1,243 tests green (Domain 646, Application 364, Architecture 13, Integration 220), no pending EF model changes. 24 demo applications in both branches through the domain (scoring engine, contract numbers, payouts, loans), backdated; five left to the live pre-check with the mock personal numbers. Every lasting status incl. escalated, committee with one vote, Pending and Failed payouts, an active loan paid on time, one with overdue installments, one closed early. Once per database, Development only (`Seed:DemoData:Enabled`). Tests: every status after the pre-check, consistent with the jobs (nothing to escalate or expire), seeded once, nothing outside Development. Decision #51. | `b9609fd` |
| P17 — README complete, final verification | done; README covers running (Docker, `.env`, user secrets, restore, migrations), the architecture choice, authentication, EF Core instead of Dapper, UoW / outbox / jobs, concurrency and idempotency, rounding, every mock with its personal numbers, accounts, demo data, the manual test script, every ambiguity and decision (#1–#51), libraries and licences, known limitations. Phase 1 table fixed (Step 8 is `67435b9`; a wrong hash had been filled in) and every placeholder replaced. Verified on a clean clone: build 0 warnings, 1,243 tests green (Domain 646, Application 364, Architecture 13, Integration 220), no pending EF model changes. | the commit "docs: complete the README and record the final verification" (it cannot name its own hash) |
| Fixes after the first demo run | done; build 0 warnings, 1,277 tests green (Domain 658, Application 382, Architecture 13, Integration 224), no pending EF model changes. Application reference `APP-000123` from a database sequence (the old one was the timestamp prefix of the id and repeated); phone numbers and email addresses masked in logs, audit and notification bodies; no multiple-collection query without split (Development and tests now throw on it). Decisions #52–#54. | the commit "fix: unique application references, masked contact data, split queries" |
