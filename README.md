# CreditFlow — retail loan origination

CreditFlow is the bank's internal system for granting loans — the bank's test task (its text is kept locally in
`docs/`, which is not committed) — built as an ASP.NET Core 9 MVC monolith. It covers the whole process: client
registration and KYC data, the application
wizard with documents, the automatic pre-check (KYC, sanctions / PEP, credit bureau), document checklist, rule-based
scoring with an explanation per rule, underwriting / credit committee / compliance decisions with four-eyes, counter-
offers, contract signing with gapless numbers, idempotent disbursement, loan booking, repayments and early repayment,
escalation and offer expiry, notifications, reports with Excel / CSV export, and the Admin area for users, roles,
products, scoring rules, thresholds, FX rates and holidays.

It continues an earlier codebase (LAMS, Phase 1: sign-in with MFA, permission-based authorization, Admin area). The
plan, every step and its progress are in [PLAN.md](PLAN.md).

**Contents:** [Running locally](#running-locally) · [Architecture](#architecture) ·
[Authentication and authorization](#authentication-and-authorization) · [Key decisions](#key-decisions) ·
[Transactions, outbox and background jobs](#transactions-outbox-and-background-jobs) ·
[Concurrency and idempotency](#concurrency-and-idempotency) · [Money and rounding](#money-and-rounding) ·
[Mocks and test personal numbers](#mocks-and-test-personal-numbers) · [Accounts and demo data](#accounts-and-demo-data) ·
[Manual test script](#manual-test-script) · [Ambiguities in the task and the decisions taken](#ambiguities-in-the-task-and-the-decisions-taken) ·
[Libraries and licences](#libraries-and-licences) · [Security behaviour](#security-behaviour) ·
[Reference data and seeding](#reference-data-and-seeding) · [Known limitations](#known-limitations)

## Running locally

**Requirements:** .NET SDK 9.0.x (`global.json`: 9.0.200, roll forward to the latest feature band) and Docker Desktop
(SQL Server, and the integration tests start their own SQL Server container).

1. **Database.** Copy `.env.example` to `.env` (git-ignored), set `MSSQL_SA_PASSWORD` (upper, lower, digit and symbol,
   at least 8 characters), then start SQL Server 2022:

   ```bash
   docker compose up -d
   ```

2. **Secrets** — user-secrets of `src/CreditFlow.Web` (nothing secret is committed):

   ```bash
   cd src/CreditFlow.Web
   dotnet user-secrets set "ConnectionStrings:CreditFlow" "Server=localhost,1433;Database=CreditFlow;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True"
   dotnet user-secrets set "Seed:Admin:Password" "<initial sysadmin password: min. 12 chars, upper/lower/digit/symbol>"
   dotnet user-secrets set "Seed:DevUsers:Password" "<password of every demo user, same rules>"
   ```

3. **Restore and run:**

   ```bash
   dotnet restore
   dotnet run --project src/CreditFlow.Web --launch-profile https
   ```

   Open <https://localhost:7180>. In Development the database is created and **migrated on start**
   (`Database:ApplyMigrationsOnStartup`), then reference data, the `sysadmin` account, the demo users and the demo
   applications are seeded (each once). `/health` answers `Healthy` without signing in.

4. **Migrations by hand** (other environments, or with start-up migration switched off):

   ```bash
   CREDITFLOW_DESIGN_CONNECTION="<connection string>" dotnet ef database update --project src/CreditFlow.Infrastructure --startup-project src/CreditFlow.Infrastructure
   ```

   `dotnet ef migrations has-pending-model-changes --project src/CreditFlow.Infrastructure --startup-project src/CreditFlow.Infrastructure`
   must report no changes (checked after every step).

5. **Tests:** `dotnet test` (Docker running; about 10 minutes, almost all of it the SQL Server integration tests).
   1,243 tests: Domain 646, Application 364, Architecture 13, Integration 220.

**Settings worth knowing** (`appsettings*.json`): `Outbox` (`Enabled`, `PollInterval`, `MaxBackoff`), `Jobs`
(`Enabled`, `Interval`), `Mocks` (`BureauTimeout`, `CoreBankingTimeout`, `HangDuration`, `Scenarios`), `DatabaseLog`
(`Enabled`, `MinimumLevel`, `BatchPeriod`), `Seed` (`Admin`, `DevUsers`, `DemoData`).

## Architecture

**Layered Clean Architecture with feature folders**, not a modular monolith — the task allows either and asks for the
reason:

- CreditFlow is one bounded context (loan origination). Client, application, disbursement and loan are used together in
  the same transactions (submission reads clients, disbursement locks parties and books the loan); modules would need
  contracts and events across boundaries that exist only on paper.
- One developer, a two-day time box: module contracts and per-module persistence would add ceremony without isolation
  benefits. Feature folders (`Applications`, `Disbursements`, `Clients`, `Reports`, `Administration`, …) inside each
  layer keep features navigable.
- The layer rules are enforced by NetArchTest (`tests/CreditFlow.ArchitectureTests`).

| Layer | Contains | Depends on |
|---|---|---|
| `CreditFlow.Domain` | Aggregates (`Client`, `LoanProduct`, `LoanApplication` with the state machine, `Disbursement`, `Loan`, `ScoringRuleSet`, `ApprovalThresholds`, `FxRate`, `Holiday`, staff/roles), value objects (`Money`, `PersonalNumber`, `LoanTerms`), pure domain services (`ScoringEngine` + one class per rule, `ScheduleCalculator`, `EffectiveRate`, `EarlyRepaymentCalculator`, `WorkingDayCalendar`), repository interfaces, domain events | nothing |
| `CreditFlow.Application` | Use cases (`ApplicationService`, `DecisionService`, `ContractService`, `DisbursementService`, `LoanService`, `ReportService`, admin services), outbox handlers, ports (`IUnitOfWork`, `ICoreBankingClient`, `IContractNumberGenerator`, `IExposureLock`, mocks' interfaces), query interfaces and read models, the permission catalog | Domain |
| `CreditFlow.Infrastructure` | EF Core (mappings, migrations, repositories, queries, interceptors), Identity, outbox dispatcher, jobs, mocks, PDF and Excel writers, seeding | Application |
| `CreditFlow.Web` | Thin MVC controllers, Razor views with htmx, authorization attributes and tag helper, Serilog setup | Application, Infrastructure (composition) |

**Rich domain:** every status transition and invariant lives in `LoanApplication` (`ApplicationStateMachine` lists the
only allowed transitions; everything else is impossible and tested over all 19 × 19 pairs). Scoring and schedule
calculation are separate, pure, unit-tested services. Business errors are `Result` / `Error` values, never exceptions.

**CQRS-lite:** one repository per aggregate root, declared in the Domain next to the aggregate and implemented with EF
Core; repositories load and save whole aggregates only. Lists, inboxes and reports are query services declared in
Application and implemented with `AsNoTracking` projections. Architecture tests check both placements and that EF Core
is referenced by Infrastructure only and controllers never touch the `DbContext`.

## Authentication and authorization

- **Method: ASP.NET Core Identity with an encrypted cookie** (`__Host-` prefixed, Secure, HttpOnly, SameSite=Strict),
  because this is a server-rendered internal application: no tokens to store in the browser, antiforgery protection on
  every POST, and server-side session control. Passwords are hashed by Identity (PBKDF2 with HMAC-SHA512, 100,000
  iterations); strong-password rules and a password history apply; temporary passwords expire after 72 hours.
- **Failed sign-ins:** 5 failures lock the account for 15 minutes, and the sign-in endpoint is rate-limited.
  MFA (authenticator app, TOTP) when the role or the user requires it. Sessions are 30 minutes sliding, at most 10 hours;
  deactivation, role or branch changes and forced sign-out end sessions within a minute (security stamp).
- **Roles are data, code checks permissions.** The seven roles of the task are seeded with a permission matrix that the
  Admin edits at runtime (Admin › Roles). Code never compares role names: `[HasPermission]` / `[HasAnyPermission]` on
  controllers and actions, the `asp-permission` tag helper hides links and buttons, and the application services check
  the same permissions again (a hidden button is never the only protection). The access profile is cached and
  invalidated explicitly when roles or users change.
- **Resource rules:** an Operator sees the applications of their branch only; the other business roles see the whole
  bank. The filter is applied **in SQL** for every list, inbox, report and export (counts and paging are right, nothing
  invisible is loaded), and every action by id loads through the same filter — an application outside the scope is
  **404**, the same answer as for an id that does not exist (no IDOR, no existence leak). An underwriter never gets an
  application they created or already decided (inbox query) and cannot decide it (domain rule). The Auditor role is
  read-only by its kind: it can hold only permissions the catalog marks read-only (enforced by the domain even against
  the Admin), and a test POSTs to every permission-protected action as the Auditor and expects 403/404 with nothing
  changed.

| Role | Code | Kind | Data scope | Permissions (seeded; editable) |
|---|---|---|---|---|
| Operator (branch) | `OP` | Business | own branch | clients, applications (create, cancel, client response), documents, contracts, disburse, loans (view, repayments), reports |
| Underwriter | `UW` | Business | bank | view clients / applications / documents / loans, decide up to the Underwriter threshold, counter-offer, reports |
| Senior Underwriter | `SUW` | Business | bank | Underwriter + decide up to the Senior threshold (incl. escalated), override a scoring rule, export reports |
| Credit Committee | `CC` | Business | bank | view, committee vote, reports |
| Compliance | `COMP` | Business | bank | view clients / applications / documents, compliance review |
| Auditor | `AUD` | Read-only | bank | every read-only permission: clients, applications, documents, loans, reports + export, audit log |
| Admin | `SYSADMIN` | Administrative | none | every `admin.*` permission (users, roles, branches, audit log, products, scoring rules, thresholds, FX rates, holidays) |

Segregation of duties: a business role cannot hold `admin.*` permissions and the Admin role holds only those, so the
Admin sees no business data.

## Key decisions

1. **EF Core instead of Dapper — agreed with the bank in writing.** The task names Dapper; the bank confirmed that EF
   Core may be used instead. What the task asks of Dapper is met with EF Core: the Unit of Work is the scoped
   `DbContext` plus an explicit transaction for multi-step use cases (`IUnitOfWork.ExecuteInTransactionAsync`; audit
   records and outbox messages are saved in the same transaction); optimistic concurrency uses SQL Server `rowversion`;
   raw SQL (locks, gapless numbering, the stage-time report, job claiming) goes only through interpolated, parameterised
   `SqlQuery` / `ExecuteSql`; reads are `AsNoTracking` projections without lazy loading, with split queries where a
   join would multiply rows. EF Core is referenced only by Infrastructure (architecture test).
2. **The roles come from the task, the mechanism from Phase 1** (permissions in the database, editable at runtime, no
   role-name checks, fail closed, cached profile with explicit invalidation). Seed unit `0004_creditflow_roles` replaced
   the LAMS roles.
3. **Role kinds enforce segregation of duties** (Business / Read-only / Administrative), see above.
4. **CQRS-lite** (repositories in the Domain, query services in Application), see above.
5. **Documents are stored in the database** (`DocumentContents`, separate from the metadata so lists never load
   content): same transaction and backup as the application. PDF, PNG and JPEG only, recognised by their first bytes
   (the name and the declared type are ignored), at most 10 MB, SHA-256 stored, downloaded as attachments.

Every other decision is listed under [Ambiguities](#ambiguities-in-the-task-and-the-decisions-taken).

## Transactions, outbox and background jobs

- **Unit of Work.** Each command loads the aggregate, applies the domain rule and saves in one transaction. Interceptors
  run inside the same `SaveChanges`: the data audit (before / after JSON of audited entities), the business audit
  (status changes, scoring runs with every rule result, decisions, overrides, escalations, payouts, repayments), the
  aggregate revision counter (below) and the **transactional outbox** (every domain event becomes an `OutboxMessages`
  row). A rolled-back transaction leaves neither audit nor outbox rows.
- **Outbox dispatcher** (`BackgroundService`): claims due messages in a short transaction with `UPDLOCK, READPAST`
  (claim expiry 5 minutes, so two dispatchers never take the same message), and runs each handler in its own
  transaction that also inserts `ProcessedMessages (MessageId, Handler)` — a redelivered message never repeats a
  handler. Failures retry with backoff 1, 2, 4 … minutes (at most 1 hour). Handlers: the pre-check (KYC, sanctions /
  PEP, bureau — an unavailable bureau throws and the message is retried, so the application waits in PreCheck and is
  never lost), scoring, notifications (status changes, escalations), the background retry of a payout that timed out,
  loan booking and closing the application when the loan is repaid.
- **Jobs** (`BackgroundService`, every `Jobs:Interval`): escalation after 2 working days in Underwriting and offer
  expiry after 30 days. Each batch claims its candidates with `UPDLOCK, READPAST, ROWLOCK` inside the transaction that
  changes them; a second run or a parallel runner finds nothing left (tested: run twice, two runners at once).

## Concurrency and idempotency

- **Optimistic concurrency.** Every aggregate root has a `rowversion` (EF shadow property) **and** a `Revision`
  counter that an interceptor increments whenever a part of the aggregate changes, so a change that only adds child rows
  (a vote, a document) still updates and checks the root. A lost race becomes the business error
  `Concurrency.Conflict`. Two underwriters approving at once: one wins, the other gets the conflict (or finds the file
  decided). Committee votes are commutative: a vote that loses the race is retried once with fresh data, so three
  simultaneous votes all count (unique index `(ApplicationId, DecidedBy)` stops double votes). Tested on SQL Server.
- **Gapless contract numbers** (`LN-2026-000123`): the next number of the bank's year comes from a counter row
  (`ContractNumberSequences`) taken with `UPDLOCK, HOLDLOCK` / `UPDATE … OUTPUT` **inside the signing transaction**; a
  failed or rolled-back signing returns its number. Tested with parallel signings and a rollback.
- **Idempotent disbursement** (`DisbursementService`, `DisbursementProcessor`): the idempotency key is derived from the
  application (`disb:{applicationId}`), so every attempt — a double click, a second browser, a retry — uses the same
  key; there is one payout row per application (unique index). Step 1 (transaction): guards, exposure lock and
  re-check, the payout saved as **Pending before core banking is called**. Step 2: core banking with the key. Success →
  Disbursed (the loan is booked by the outbox); rejection → Failed, a retry reuses the key; timeout → stays Pending and
  the outbox asks again with the same key, which returns the original transaction id. Tested: twice in a row, twice in
  parallel, error then retry, timeout then background confirmation.
- **Exposure limit without races** (`SqlExposureLock`): inside the disbursement transaction an exclusive
  `sp_getapplock` is taken per party (`exposure:{clientId}`, in ascending id order so payouts sharing parties cannot
  deadlock), then the committed exposure is re-checked. Two parallel payouts that only fit one at a time run one after
  the other; exactly one succeeds (tested with two loans sharing a guarantor).

## Money and rounding

`decimal` only. **One rounding rule** in `Money.Round`: 2 decimals, `MidpointRounding.AwayFromZero`, applied when a
`Money` is created. Interest is computed per period on the remaining principal and rounded once per installment; the
**last installment absorbs every rounding difference**, so the principal parts add up exactly to the loan amount.
Annuity and differentiated schedules; monthly installments, the first one month after disbursement, interest = annual
rate / 12. The **effective annual rate** includes the upfront and monthly fees: the monthly IRR of the client's cash
flows (amount received = principal − upfront fee; payments = installments + fees), solved by bisection in `decimal`
and annualised as (1 + i)^12 − 1; shown on the loan, the contract and the schedule. Amounts in foreign currency are
converted to GEL with the latest rate on or before the date (rates are `decimal` with 6 places). Rule comparisons use
exact values; measured values are rounded for display only.

## Mocks and test personal numbers

Every external system is an interface in Application with a mock in `Infrastructure/ExternalServices`. Scenarios are
chosen by the personal number of the client: the seeded catalog below (`MockPersonalNumbers`) plus any you add in
configuration, e.g. `"Mocks:Scenarios:01001011099:0": "BureauUnavailable"`. Timeouts are real: a hanging call is
cancelled after `Mocks:BureauTimeout` / `Mocks:CoreBankingTimeout`.

| Mock | What it does |
|---|---|
| Sanctions / PEP (`ISanctionsScreeningService`) | List in the database (`SanctionsListEntries`, seed unit `0006`): by personal number, or by full name ("Ivan Petrov") |
| Credit bureau (`ICreditBureauClient`) | Late payments and obligations at other banks; unavailable and timeout scenarios |
| KYC / identity (`IIdentityVerificationService`) | Verifies the document data the operator entered |
| Core banking (`ICoreBankingClient`) | Returns a transaction id; executes each idempotency key once (its own ledger, `MockCoreBankingTransactions`); error and timeout scenarios |
| Email / SMS (`INotificationSender`) | Writes `Notifications` rows and a log line instead of sending |
| Collateral valuation, registration, insurance | Not a service: entered by the operator (value, date; conditions marked met) |
| FX rates | Table `FxRates`, edited by the Admin or imported from CSV |
| Contract signing | The operator marks the contract signed |

| Personal number | Scenario |
|---|---|
| `01001011001` | clean: every check passes |
| `01001011002` | sanctions match → ComplianceReview |
| `01001011003` | PEP match → ComplianceReview |
| `01001011004` | KYC: identity document not verified → Rejected |
| `01001011005` | bureau: 45 days late three months ago → rejected by scoring (`BUREAU_DPD30_12M`) |
| `01001011006` | bureau always unavailable → stays in PreCheck, retried in the background |
| `01001011007` | bureau times out twice, then answers → the background retry succeeds |
| `01001011008` | bureau: 2,500 GEL monthly obligations at other banks → PTI fails (overridable) |
| `01001011009` | core banking rejects the first payout of a key → Failed; a retry with the same key succeeds |
| `01001011010` | core banking executes the first payout but answers too late → Pending; the retry returns the same transaction |

Any other personal number behaves like the clean one.

## Accounts and demo data

There is no self-registration. The Admin creates accounts (Admin › Users) and receives a temporary password (shown
once, valid 72 hours) that the user changes at first sign-in.

- **Admin** — seeded in every environment from configuration: user name `sysadmin` (`Seed:Admin:UserName`), password
  from the user secret **`Seed:Admin:Password`**. It must change the password at first sign-in, lands on the administration dashboard (locked users, users without MFA, recent access changes)
  and sees no business data.
- **Demo personas** — Development only (`Seed:DevUsers:Enabled`), all with the password from the user secret
  **`Seed:DevUsers:Password`**, no forced change, no MFA. Sign in with the user name or the email.

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

- **Demo applications** — Development only (`Seed:DemoData:Enabled`), once per database: 24 applications in both
  branches, built through the domain (real scoring runs, contract numbers, payouts, loans) and dated back: a draft;
  PreCheck (bureau down), two in ComplianceReview (sanctions, PEP), DocumentsPending (clean) — these five are submitted
  and left to the live pre-check; Underwriting (plain, Senior tier, escalated); CreditCommittee with one of two votes;
  CounterOffered; Approved; ConditionallyApproved; ContractSigned with open mortgage conditions; ConditionsMet ready to
  pay out, with a Pending payout (`01001011010`) and with a Failed payout (`01001011009`); an Active loan paid on time,
  one with overdue installments, one Closed after early repayment; Rejected (KYC, scoring rule, underwriter);
  Cancelled; Expired. Submitted, Scoring and Disbursed last seconds (the background pipeline moves them on) and are
  visible in every history.

## Manual test script

Run in Development (step 3 of [Running locally](#running-locally)). The background pipeline needs a few seconds after
submitting or sending to scoring — refresh the page.

1. **anna.gelashvili** (Operator, Vake): Clients › Register client — personal number `01001011001` (or any new
   11-digit number), born 1990-05-17, ID card valid until 2031. Add a monthly income: Salary 3,000 GEL.
2. **Create application**: the personal number, Consumer loan, 5,000 GEL, 24 months, 18 %, Annuity → **Create draft**.
   Wizard step 2: Next. Step 3: upload any PDF as *Identity document copy* and another as *Income statement*. Step 4:
   **Submit**. Refresh: *Documents pending*. **Send to scoring**, refresh: *Underwriting*.
3. **nika.beridze** (Underwriter): Inbox → open the application → check the scoring card → **Approve**.
4. **anna.gelashvili**: **Contract (PDF)** → **Mark contract signed** (number `LN-2026-…`, status *Conditions met*) →
   **Disburse** (*Paid out*; refresh: *Active*) → **Open the loan** → **Next installment paid** → **Quote full
   repayment** → **Record early repayment**: the loan is *Closed*, and after a refresh the application too.
5. **levan.abashidze** (Auditor): sees everything including PDFs and reports, but no action buttons.
   **davit.maisuradze** (Operator, Batumi): the Vake application and loan are not listed, and their URLs answer 404.
6. Variants (as an operator):
   - Compliance: a borrower `01001011002` stops in *Compliance review*; **nino.kvaratskhelia** clears it from
     Inbox › Compliance.
   - Payout rejected: borrower `01001011009` → Disburse shows *Rejected by core banking* → **Retry payout** pays it.
   - Payout timeout: borrower `01001011010` → *Waiting for core banking*; it confirms itself in the background, or click
     **Check payout again**.
   - Committee: Mortgage 150,000 GEL, 120 months, 10 %; the client needs about 5,000 GEL income (PTI); real-estate
     collateral worth 250,000 valued within 6 months; documents *Identity document copy*, *Income statement*, *Property
     valuation report*, *Cadastral extract*. **irakli.tsereteli** and **sophio.gogichaishvili** vote Approve from their
     inbox (tally 1/0, then 2/0); mark the two mortgage conditions met; disburse.
7. **sysadmin** (Admin): Admin › Products (publish a new version), Scoring rules (publish a version with a stricter
   parameter), Approval thresholds, FX rates (CSV import), Holidays, Audit log (every change above is there).
8. Reports (any role with reports): filter by period and product; **tamta.tatishvili** or the Auditor export to
   Excel / CSV; an Operator sees only their branch's figures and has no export.

## Ambiguities in the task and the decisions taken

The full list with the reasoning is §P.4 of [PLAN.md](PLAN.md); in short:

1. **ConditionallyApproved vs Approved** — a decider may attach conditions (insurance, an extra document); with any →
   ConditionallyApproved, without → Approved. A product's standard conditions (mortgage / auto: collateral registration
   and insurance) are added to every approval. Both go through ContractSigned → ConditionsMet; without conditions
   ConditionsMet follows the signing automatically (both recorded).
2. **Disbursed vs Active** — Disbursed = core banking confirmed the transfer (saved at once); Active = loan and schedule
   booked (outbox, retryable, idempotent); Closed = outstanding principal 0.
3. **Offer validity / client consent** — the client accepts by signing within 30 calendar days of the decision; a
   counter-offer must be accepted within 30 days too, otherwise Expired. A declined counter-offer → Cancelled.
4. **Co-borrowers and guarantors** — every party gets KYC, sanctions / PEP, bureau, age and late-payment checks.
   Exposure: every party counts the full amount (a guarantee covers the whole loan).
5. **PTI thresholds** (net monthly income in GEL): < 1,000 → 35 %; 1,000–3,000 → 45 %; ≥ 3,000 → 55 %. Obligations =
   bureau (other banks) + our active loans + declared. Configurable in the rule set.
6. **FX stress** — for USD / EUR loans the new installment in GEL is multiplied by 1.20 in PTI.
7. **FX threshold** — a USD / EUR loan below 200,000 GEL requires income in the loan currency (configurable).
8. **Exposure limit** — 300,000 GEL per client (configurable, `EXPOSURE_LIMIT`).
9. **Thresholds currency** — approval thresholds and limits are GEL; foreign amounts use the latest rate on or before
   the scoring date, stored with the scoring run (routing does not drift with later rates).
10. **Rule failure vs override** — a failed hard rule rejects automatically; an overridable failure (PTI) continues
    flagged and approval then needs `scoring.override` and a reason (audited).
11. **Four-eyes** — one decision per person per application over its whole life (compliance, underwriting,
    counter-offer, vote); the creator never decides; after a counter-offer a different underwriter decides.
    Unique index `(ApplicationId, DecidedBy)`.
12. **Committee** — members = active users with `committee.vote` (3 seeded); 2 matching votes decide (configurable).
13. **Escalation** — after 2 working days in Underwriting (Asia/Tbilisi, weekends and `Holidays` excluded) the file is
    flagged, the Senior Underwriters are notified, and only `underwriting.decide-senior` can decide it. Escalation is a
    flag, not a status.
14. **Rounding** — see [Money and rounding](#money-and-rounding).
15. **Schedule conventions** — monthly, first installment one month after disbursement, annual rate / 12.
16. **Early repayment** — partial: the remaining term stays and the installments are recalculated on the lower
    balance; full: outstanding principal + interest accrued since the last due date (actual / 365) → Closed. Penalty =
    the product's % of the principal repaid early (seed: consumer 2 %, auto and mortgage 1 %). Refused while an
    installment is overdue (record it first).
17. **Repayments** — no collection system: an operator records them (`loans.record-repayment`); "next installment
    paid" pays the oldest unpaid installment in full.
18. **Document storage** — in the database, see decision 5 above.
19. **Bureau unavailable** — the pre-check message is retried with exponential backoff (1, 2, 4 … minutes, at most 1
    hour); the application stays in PreCheck and is never lost.
20. **Open application** — open = Draft … ConditionsMet (one per borrower and product, filtered unique index).
21. **Age at the last installment** — checked at submission with the requested term and again at disbursement.
22. **Cancel** — allowed Draft … ConditionsMet with a reason, but not once a payout is pending (money may be moving).
23. **Operator scope** — Operators see all applications of their branch; the branch is the creator's branch.
24. **Out of scope by id** → 404, not 403.
25. **Who disburses** — the Operator requests it; the system executes it.
26. **Notification recipients** — status changes → the creating operator (email) and the borrower (SMS) for the
    changes a client cares about; escalation → active Senior Underwriters; expiry → operator and client.
27. **Product amount limits are GEL** — a foreign-currency loan is checked by its GEL equivalent at submission.
28. **Fees** — upfront fee % of the principal (deducted from the payout, part of the effective rate); monthly service
    fee % of the original principal per month.
29. **Regulatory LTV ceilings are code** (mortgage 80 %, auto 70 %); a product version may be stricter, never looser.
30. **Concurrency tokens are shadow properties** — the domain model stays free of persistence details.
31. **Bank time** — business dates (age, document validity, holidays, working days, contract year, report periods)
    are dates in Asia/Tbilisi (UTC+4); instants are stored in UTC.
32. **Age rules are fixed policy**, not scoring parameters: ≥ 18 at application and ≤ 75 at the last installment,
    every party, all errors at once.
33. **Committee approvals** carry the product's standard conditions only; a vote is Approve or Reject (reason).
34. **Counter-offer** keeps currency and schedule type; amount, term and rate must fit the snapshot; accepting it needs
    the checklist for the new amount.
35. **Scoring rules: data vs code** — the database holds which rules a version uses, their parameters and whether a
    failure can be overridden; each rule's logic is code, so a version is reproducible. Every run stores the version,
    each rule's result, measured value, threshold and reason.
36. **Effective rate** — monthly IRR of the client's cash flows, annualised, bisection in `decimal`.
37. **Aggregate-wide concurrency** — `rowversion` plus `Revision`, see above.
38. **Value objects** — required ones as EF complex types (plain columns); optional or list-shaped ones as JSON columns,
    never queried in SQL.
39. **Outbox** — see above; the bureau retry of 19 is the outbox backoff, no separate job.
40. **Mocks** — scenarios by personal number, overridable in configuration; real timeouts.
41. **Exposure at disbursement counts committed money only** — payouts pending or done and the outstanding principal
    of active loans (GEL at each application's scoring rate) plus the new loan; approvals never paid out do not block a
    payout (scoring still counts them, conservatively). The limit is the one of the rule-set version the application
    was scored with.
42. **Payout amount** — the principal less the upfront fee; the schedule runs on the full principal.
43. **Core banking mock** — its ledger lives in its own scope (like a separate system); `01001011009` rejects the
    first request of a key, `01001011010` executes the first request but answers too late.
44. **Repayments** — see 16 and 17.
45. **Jobs** — one service for escalation and expiry; candidates claimed with `UPDLOCK, READPAST, ROWLOCK` per batch,
    keyset by id; running twice or in parallel gives one effect.
46. **PDFs** — the contract can be printed from the approval on ("number assigned at signing" until then); the
    schedule is projected from today before disbursement and is the loan's schedule afterwards; `contracts.manage` or
    `loans.view`.
47. **Wizard** — step 1 creates the draft, steps 2–4 edit it through the normal actions (no second write path); a
    draft's checklist comes from the product's current version, the snapshot rules from submission on.
48. **Payout on the page** — Pending shows "check again", Failed "retry"; both post the same idempotent action.
49. **Reference-data admin** — product terms and rule sets are append-only versions (the newest is in force);
    thresholds, FX rates and holidays are edited in place with before / after in the audit; an FX import is all or
    nothing.
50. **Reports** — period = applications created, decisions taken, stages left, loans disbursed, escalations in the
    period; portfolio and overdue as of today; GEL at the scoring rate; a scoring rejection counts once per failed
    rule; exports need `reports.export` (not held by Operators).
51. **Demo data** — Development only, once per database, built through the domain; see above.
52. **Application reference** — `APP-000123`: a number from a database sequence (`ApplicationNumbers`), assigned when
    the application is first saved, unique, searchable in the application list. Ids are time-ordered GUIDs, whose start
    is a timestamp, so no part of an id is used as a reference (the audit log abbreviates ids by their random end).

## Libraries and licences

All licences allow commercial use.

| Library | Use | Licence |
|---|---|---|
| ASP.NET Core 9, EF Core 9 (SQL Server), ASP.NET Core Identity, `Microsoft.Extensions.*` | framework, persistence, accounts | MIT |
| FluentValidation | command validation | Apache-2.0 |
| Serilog.AspNetCore, Serilog.Sinks.MSSqlServer | logging to console and the `Logs` table | Apache-2.0 |
| QRCoder | QR code for authenticator enrolment | MIT |
| PDFsharp-MigraDoc 6.2 | contract and schedule PDFs | MIT |
| DejaVu Sans fonts (embedded in PDFs) | Latin and Georgian glyphs | Bitstream Vera licence / public domain (`src/CreditFlow.Infrastructure/Documents/Fonts/DejaVu-LICENSE.txt`) |
| ClosedXML 0.105 (+ DocumentFormat.OpenXml, ExcelNumberFormat, RBush, ClosedXML.Parser) | Excel export | MIT |
| SixLabors.Fonts 1.0 (ClosedXML dependency) | text measuring | Apache-2.0 (1.x, before the split licence) |
| htmx 2.0 (self-hosted) | partial page updates | 0BSD |
| Inter, JetBrains Mono (self-hosted) | UI fonts | SIL OFL 1.1 |
| xUnit v3, NetArchTest, Testcontainers, `Microsoft.AspNetCore.Mvc.Testing` | tests | Apache-2.0 / MIT |

Not used on purpose: QuestPDF (its Community licence excludes companies above USD 1M revenue) and EPPlus (commercial).
CSV is written by a small RFC 4180 writer that neutralises spreadsheet formulas (CSV injection).

## Security behaviour

- Sessions: 30 minutes sliding, at most 10 hours after sign-in (a password change does not extend it). Deactivation,
  forced sign-out, password reset and changes to role or branch end open sessions within one minute (security stamp
  validation). Permission changes apply on the next request.
- Lockout: 5 failed attempts lock the account for 15 minutes (the Admin can unlock it). As in ASP.NET Core Identity,
  a locked account shows the lockout page even when the correct password is entered; this reveals that the account
  exists, which is accepted for an internal staff system. Unknown user, wrong password and inactive account all show
  the same message; the audit log records the real reason.
- MFA (authenticator app) is required when the role or the user requires it; enrolment is then forced at sign-in.
- Every sign-in, security event, administrative change and business event (status changes, scoring runs, decisions,
  overrides, payouts, repayments) is in the audit log, written in the same transaction as the change. It is
  append-only in code and in the database (trigger). The Admin and the Auditor can read it.
- Logs go to the console and to the `Logs` table (time, level, message, exception, user, request path, correlation id,
  other properties as JSON); the table is created by an EF Core migration, not by the sink.
- **Masking.** One rule (`SensitiveData`) serves logs, audit records and notification bodies: any 11-digit number (a
  personal number) keeps only its last four digits (`*******1001`); email addresses keep the first character and the
  domain (`a***@pcb.ge`); phone numbers (`+995 599 011011`, or a Georgian mobile `599 01 10 11`) keep their last two
  digits (`+*** *** ****11`); fields named like a password, token, key or recovery code are hidden; personal / document
  number fields keep their last four characters; phone, email and recipient fields are always masked. In logs, a
  wrapping sink masks every event before any sink sees it — template text, property values at any depth and exception
  text.
- Content Security Policy `'self'` only (htmx and fonts are self-hosted), antiforgery on every POST, security headers.
- **Data Protection keys** (they protect the auth and antiforgery cookies) are stored in the database and encrypted at
  rest: with the certificate in `DataProtection:CertificatePath` / `DataProtection:CertificatePassword` when set
  (servers, containers), otherwise with DPAPI on Windows.
- **Warnings mean something.** A click-through of every page logs no warning; an external service that is down or
  slow logs one line per scheduled retry ("… will be retried …"), without a stack trace. EF Core query warnings
  (unordered row limits, `First` without `OrderBy`, multi-collection includes, decimals without a store type) throw in
  Development and tests.
- Lists, inboxes, reports and exports are paged / filtered / sorted on the server with parameterised SQL; indexes exist
  for the operator lists, the inboxes, the jobs and the reports.

## Reference data and seeding

Reference data is seeded at start-up in every environment by `ReferenceDataSeeder`
(`src/CreditFlow.Infrastructure/Persistence/Seeding`). Seeding is **versioned**:

- Every change to reference data is a **seed unit** (`ISeedUnit`) with an ordered id `NNNN_snake_case_description`,
  listed in order in `SeedUnits.All`. Each pending unit runs **once**, in its own transaction, recorded in
  `SeedHistory`.
- A unit that has run never runs again: from then on the Admin owns the data (removed permissions, deleted branches,
  changed products are never brought back by a restart). Units are idempotent.
- The fixed definition of each role (name, kind, data scope, dashboard, branch rule) is code-owned and re-applied on
  every start; permissions and the "require MFA" flag belong to the Admin.

| Seed id | What it does |
|---|---|
| `0001_initial_roles_permissions` | Retired (created the LAMS roles; replaced by `0004`). |
| `0002_initial_branches` | The 6 branches (`BranchSeedCatalog`). |
| `0003_initial_authority_limits` | Retired (LAMS authority limits; the table was dropped). |
| `0004_creditflow_roles` | The seven CreditFlow roles with the matrix from `RoleSeedCatalog`; converts a Phase 1 database (Loan Officers → Operator, other LAMS roles → Auditor, deactivated). |
| `0005_lending_reference_data` | Products (consumer with two versions, mortgage, auto), scoring rule set version 1, approval thresholds, FX rates, Georgian public holidays 2026–2027. |
| `0006_mock_sanctions_list` | The mock sanctions / PEP list. |

Development additionally seeds the demo users and the demo applications (`dev_0001_lending_demo_data`), see
[Accounts and demo data](#accounts-and-demo-data).

Rules: never edit, rename, reorder or delete a unit that may have run anywhere — append a new one. A unit may be
**retired** only when a later unit replaces everything it created. Use the idempotent helpers in `SeedOperations`; they
reject unknown permissions and grants that break segregation of duties. Code never checks role codes.

**Adding a role:** add the code to `RoleCodes`; new permissions to `Permissions` / `PermissionCatalog`; a `RoleSeed`
to `RoleSeedCatalog`; append a unit calling `SeedOperations.CreateRoleIfMissingAsync`; update
`PermissionMatrixTests`, `ReferenceDataTests` and `SeedHistoryTests`. **Granting a permission to an existing role:**
add it to the role in `RoleSeedCatalog` and append a unit calling `SeedOperations.GrantAsync`.

## Known limitations

- **Single instance, local.** Behind a reverse proxy, forwarded headers must be configured first (audit IP addresses
  are the direct peer). The jobs and the outbox are safe with several instances (row claiming), but this was only
  tested with parallel runners in one process.
- **Mocks are mocks.** The core banking error scenario counts requests per key in memory, so after a restart the first
  retry of a seeded Failed payout is rejected once more. Notifications are rows and log lines, not emails or SMS.
- **Repayments are recorded by hand** and only as whole installments or early repayments; there is no penalty interest
  on arrears and no partial payment of an installment.
- **Reports** use the GEL amount fixed at scoring (no revaluation at today's rate); figures are computed on request (no
  caching or pre-aggregation), fine for the data volumes of this task.
- **Rule-set parameters** are edited as JSON in Admin › Scoring rules (validated by the engine); two administrators
  publishing at the same instant get an error for the second publish rather than a friendly conflict message.
- **PDFs** are plain documents without letterhead or legal clauses.
- **Language:** English; a Georgian culture is registered and falls back to English. UI fonts have no Georgian glyphs
  (the browser's system font is used for Georgian text); PDFs embed fonts that do.
- **Demo data:** Submitted, Scoring and Disbursed are passing states and are only visible in histories.
- **Tests** take about 10 minutes because every integration test class gets its own SQL Server database.
