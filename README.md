# PaymentFunds

An ASP.NET Core MVC application for submitting payment requests, routing them through an approval gate, and processing approved requests asynchronously against the Stripe API.

Built to work through the patterns real payment systems depend on: explicit state machines, idempotency at every retry boundary, asynchronous processing that survives restarts, and authenticated webhooks.

> **Test mode only.** This app runs against Stripe's test environment. See [Current limitations](#current-limitations).

---

## Table of contents

- [How it works](#how-it-works)
- [Tech stack](#tech-stack)
- [Architecture](#architecture)
- [Authentication and roles](#authentication-and-roles)
- [Payees and disbursements](#payees-and-disbursements)
- [The ledger](#the-ledger)
- [Key design decisions](#key-design-decisions)
- [Getting started](#getting-started)
- [Running the tests](#running-the-tests)
- [Project structure](#project-structure)
- [Data model](#data-model)
- [Current limitations](#current-limitations)
- [Roadmap](#roadmap)

---

## How it works

A payment request moves through an explicit state machine. Nothing skips a step, and every transition is guarded server-side.

![How PaymentFunds works](src/PaymentFunds/PaymentFunds-how-it-works.svg)

1. **Submit.** A signed-in user picks a request type, an amount and currency, and for a disbursement a verified payee. The requester is taken from their authenticated identity, never from form input. The server mints an idempotency key when it renders the form, so resubmitting the same form cannot create a duplicate.
2. **Approve.** A user in the `Approver` role approves or rejects, and cannot act on their own request. Both actions are recorded against the authenticated user with a timestamp. Only `PendingApproval` requests can be acted on.
3. **Process.** A background worker polls for `Approved` rows every five seconds, claims one atomically, re-checks that the payee is still verified, and calls Stripe. The request's idempotency key is forwarded to Stripe so a retry cannot double-charge.
4. **Settle.** Stripe calls back over a webhook when the payment resolves. The signature is verified, the event is deduplicated, and the request moves to `Completed` or `Failed`.

Approval and settlement each write balanced ledger entries in the same transaction as the status change, so the financial record and the operational record cannot drift apart. See [The ledger](#the-ledger).

The web request never waits on Stripe. Approval returns immediately and the payment happens out of band.

---

## Tech stack

| Concern | Choice |
|---|---|
| Framework | ASP.NET Core 10 (MVC) |
| Database | PostgreSQL 16 |
| ORM | EF Core 10 with Npgsql |
| Payments | Stripe (Stripe.net 52.x) behind an `IPaymentProcessor` seam |
| Async processing | `BackgroundService` polling the database |
| Auth | ASP.NET Core Identity, cookie-based, role authorization |
| Testing | NUnit 4 with `WebApplicationFactory` integration tests |
| Orchestration | Kubernetes (minikube locally) |
| Secrets (dev) | .NET User Secrets, sops for Kubernetes manifests |

---

## Architecture

![Architecture](src/PaymentFunds/paymentfunds-architecture.svg)

The app has three independent entry points that never call each other:

- **HTTP from a person** reaches `PaymentRequestsController`, behind `[Authorize]`.
- **A timer** drives `PaymentProcessingWorker`, which starts with the app and polls forever.
- **HTTP from Stripe** reaches `StripeWebhookController`, which is `[AllowAnonymous]` and authenticated by signature instead.

They coordinate entirely through rows in Postgres. The `Status` column is the message: setting `Approved` posts a job, and the worker flipping it to `Processing` claims it. `ProviderReference` is the join key an inbound webhook uses to find the request it refers to.

---

## Authentication and roles

Users are provisioned, not self-registered. An internal disbursement tool should not let anyone sign themselves up and pick the `Approver` role, so there is no registration page. Two roles exist:

| Role | Can do |
|---|---|
| `Requester` | Sign in, create requests, view any request |
| `Approver` | The above, plus approve and reject requests they did not raise |

Enforcement lives on the server in three places:

- `[Authorize]` on `PaymentRequestsController` means every action requires a signed-in user.
- `[Authorize(Roles = "Approver")]` on `Approve` and `Reject`.
- An explicit check that `RequestedBy != User.Identity.Name`, returning `403`.

That last one is separation of duties: the person requesting money must not be the person authorising it. The details view also hides the buttons in those cases, but that is a convenience. Hand-crafting the POST still gets refused.

`ApprovedBy`, `RejectedBy`, and `RequestedBy` are all read from the authenticated principal. Before this, they were free-text form fields, which meant the audit trail recorded a claim rather than a fact.

The Stripe webhook endpoint is explicitly `[AllowAnonymous]`. Stripe has no cookie and no account, so requiring authorization there would turn every delivery into a redirect to the login page and Stripe would retry for three days while nothing settled.

Development accounts are seeded at startup from configuration:

| Account | Role |
|---|---|
| `requester@paymentfunds.local` | `Requester` |
| `approver@paymentfunds.local` | `Approver` |

Their passwords come from user secrets rather than source, and seeding is skipped with a warning if they are not set.

---

## Payees and disbursements

A request records two different people. `RequestedBy` is who asked for the money. The `Payee` is who receives it. Conflating those is what makes a workflow demo rather than a disbursement system.

`RequestType` distinguishes a **Collection** (money in, which is what a Stripe `PaymentIntent` actually does) from a **Disbursement** (money out). Only disbursements require a payee.

### Payee lifecycle

| Status | Meaning |
|---|---|
| `Unverified` | Created, cannot receive money |
| `Verified` | Payout destination confirmed, eligible |
| `Suspended` | Blocked, including for requests already approved |

Any signed-in user can add a payee. Only an `Approver` can verify one, and **not one they created themselves**.

That restriction is the point of the feature. Creating a fake vendor and verifying it is the classic accounts-payable fraud, and it doesn't require compromising the payment approval at all: stand up "Acme Consulting" pointing at your own account, verify it, then raise requests that a second person approves in good faith. The approval was real; the payee was not. So payee verification carries the same separation of duties as payment approval.

Suspension is deliberately unrestricted. Blocking money movement is always allowed; only enabling it is gated.

Verification also requires `TaxFormOnFile`, standing in for the compliance checks a real system would run before paying anyone.

### No raw bank details, anywhere

A `Payee` stores an opaque `ProviderAccountReference` and a masked `PayoutDestinationMask` such as `••••4321`. There is no account number, routing number, or IBAN in the schema, and the create form accepts **only four digits**, so a full account number cannot be submitted even deliberately.

Same reasoning as card data. Holding raw bank credentials creates compliance obligations there's no reason to take on, and a breach that ends the company. The correct answer to "where do you store bank details" is that you don't.

### The verification check happens twice

A payee verified when a request was raised can be suspended before the worker pays it, and requests can sit awaiting approval for a while. Checking once at submission and acting later means acting on stale information, which is a time-of-check to time-of-use gap.

So the guard runs in both places: `PaymentRequestsController.Create` refuses to create a disbursement to an unverified payee, and `PaymentProcessingWorker` re-reads the payee immediately before calling Stripe and fails the request if the status has changed. The last line of defence sits closest to the money.

---

## The ledger

Every money movement is recorded twice, as a debit on one account and a credit on
another, and the two always cancel. Money cannot appear or vanish, only move. Balances
are derived by summing an append-only history rather than tracked in a mutable column.

### Accounts

Four account codes, seeded per currency (`usd`, `eur`, `gbp`), plus an opening-balance
account used to fund the platform:

| Code | Type | Holds |
|---|---|---|
| `CASH` | Asset | Money the platform holds |
| `PAYABLE` | Liability | Money owed to payees but not yet sent |
| `EXPENSE` | Expense | Cost of disbursements |
| `REVENUE` | Revenue | Money collected |
| `OPENING` | Equity | Counterpart for platform funding |

An account holds exactly one currency. Adding dollars to euros is meaningless, so a
posting cannot span currencies.

### What each state transition posts

| Event | Debit | Credit |
|---|---|---|
| Disbursement approved | `EXPENSE` | `PAYABLE` |
| Disbursement settled | `PAYABLE` | `CASH` |
| Disbursement failed after approval | `PAYABLE` | `EXPENSE` |
| Collection settled | `CASH` | `REVENUE` |

Approval and settlement are separate postings because owing money and paying it are
different facts on different dates. The two-step state machine already modelled that
before the ledger existed; the ledger records its financial meaning.

### Amounts are integers, and always positive

`LedgerEntry.AmountMinor` is a `long` holding minor units. Summing integers is exact;
summing decimals invites rounding arguments. `PaymentRequest.Amount` stays `decimal`
for display, and conversion happens at the boundary.

There are no negative entries. `Direction` carries the sign, which is what makes "do
debits equal credits?" a checkable question rather than a convention. A sign error
becomes visible instead of silently cancelling out. A `CK_LedgerEntry_PositiveAmount`
check constraint enforces it at the database.

### Postings commit with the state change they describe

`ILedgerService.AddPostingAsync` deliberately does **not** call `SaveChangesAsync`. It
stages entries on the current `DbContext` and the caller saves, so the status change
and the ledger entries land in one transaction.

Saving separately would mean a crash could leave a request marked `Approved` with no
obligation recorded, and the ledger would quietly disagree with operational data. Same
reasoning as the database-as-queue decision: one system, one transaction, nothing to
reconcile.

### Failures reverse, they never edit

A failed disbursement posts a new, opposite pair rather than editing or deleting the
approval entries. The history then reads "we owed this, then we didn't," which is what
happened. An append-only ledger is trustworthy precisely because nothing in it can be
changed after the fact.

### Insufficient funds is a real state

Available funds is **cash less outstanding payables**, not just cash. An approved but
unsettled disbursement has already committed that money even though it hasn't left
yet, so ignoring payables would allow approving the same pound twice.

Approval is refused when the amount exceeds available funds, and the check runs before
any mutation, so a refused approval leaves no trace.

---

## Key design decisions

### The database is the queue

Approved requests are not pushed onto an in-memory queue. The worker polls Postgres for rows in `Approved` state.

An in-memory queue (`System.Threading.Channels`) is the more common .NET answer and is simpler, but it lives in pod memory. A restart between "approved" and "paid" loses the work silently. Pods restart routinely in Kubernetes during rollouts, evictions, and node drains, so that failure mode is not acceptable for disbursements. Polling a durable table survives restarts with no extra infrastructure.

### Work is claimed atomically

```csharp
var claimed = await db.PaymentRequests
    .Where(pr => pr.Id == id && pr.Status == PaymentStatus.Approved)
    .ExecuteUpdateAsync(set => set.SetProperty(pr => pr.Status, PaymentStatus.Processing), ct);

if (claimed == 0) continue;
```

This compiles to a single `UPDATE ... WHERE`. Postgres evaluates the predicate under the row lock, so if two workers race, exactly one gets `1` back. This is what allows scaling to multiple replicas without paying anyone twice.

### Idempotency at all three boundaries

The same concept appears wherever a message can be retried:

| Boundary | Key | Enforced by |
|---|---|---|
| Browser → app | GUID minted when the form renders, carried in a hidden field | Pre-check plus a unique index on `IdempotencyKey` |
| App → Stripe | The same GUID, passed as `RequestOptions.IdempotencyKey` | Stripe replays the original response |
| Stripe → app | Stripe's event id | `ProcessedStripeEvents` table with a unique index |

On the inbound side, the pre-check handles the common case and the unique index closes the race the pre-check cannot. Application logic proposes; the database enforces.

### Webhook signatures are verified over the raw body

The handler reads `Request.Body` as a stream rather than binding to a model. Stripe signs the exact bytes it sent, so deserializing and re-serializing would shift whitespace and key order and break verification. A forged or altered payload gets a 400 and never touches a payment record.

### Scoped services inside a singleton worker

`AddHostedService` registers the worker as a singleton, while `DbContext` is scoped. Injecting the context directly would leave one instance alive for the life of the process. The worker injects `IServiceProvider` and creates a scope per iteration instead.

---

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [minikube](https://minikube.sigs.k8s.io/docs/start) and `kubectl`
- [Stripe CLI](https://docs.stripe.com/cli)
- A Stripe account (test mode)
- Nothing else listening on port 5432

All `dotnet` commands are run from the repository root and target the app with
`--project src/PaymentFunds`.

You will want four terminals: one for the port-forward, one for `stripe listen`,
one for the app, and one to work in.

### 1. Start PostgreSQL in the cluster

```bash
minikube start
kubectl apply -f k8s/
kubectl get pods -w        # wait for 1/1 Running
```

Forward the database port so the app can reach it from your host. Leave this running
in its own terminal; it does not survive a cluster restart.

```bash
kubectl port-forward svc/postgres 5432:5432
```

### 2. Configure secrets

```bash
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..." --project src/PaymentFunds
dotnet user-secrets set "SeedUsers:RequesterPassword" "<a password>" --project src/PaymentFunds
dotnet user-secrets set "SeedUsers:ApproverPassword" "<a password>" --project src/PaymentFunds
```

The seed passwords are read from configuration rather than hardcoded, so no default
credentials live in source control. Without them the seeder logs a warning and skips
user creation.

Copy `src/PaymentFunds/appsettings.Development.example.json` to
`appsettings.Development.json` and fill in the connection string. That file is
gitignored, and its credentials must match `k8s/postgres-secret.yaml`, which is
encrypted with sops.

The test project reads its own connection string, also from user secrets, with no
hardcoded fallback:

```bash
dotnet user-secrets set "ConnectionStrings:TestDatabase" \
  "Host=localhost;Port=5432;Database=paymentfunds_test;Username=<user>;Password=<password>" \
  --project tests/PaymentFunds.Tests
```

### 3. Apply migrations

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project src/PaymentFunds
```

The PersistentVolumeClaim survives `minikube stop`, so on a normal restart the schema
is already there and this is a no-op. After a `minikube delete` the volume is gone and
this recreates everything.

Check what you have with:

```bash
psql -h localhost -p 5432 -U brandon -d PaymentFunds -c '\dt'
```

### 4. Start the webhook listener

In a separate terminal:

```bash
stripe listen --forward-to localhost:5245/api/stripe/webhook
```

Copy the `whsec_` secret it prints and compare it with what is already stored:

```bash
dotnet user-secrets list --project src/PaymentFunds
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..." --project src/PaymentFunds
```

> Restart the app after changing user secrets. Configuration binds at startup, so a
> running app keeps the old value. A stale signing secret shows up as a 400 on every
> forwarded event.

### 5. Run

```bash
dotnet run --project src/PaymentFunds
```

Open <http://localhost:5245/PaymentRequests>.

Sign in at <http://localhost:5245/Account/Login> with the seeded accounts and the
passwords from step 2.

### 6. Verify end to end

The flow needs two different people, since an approver cannot approve their own
request:

1. Sign in as `requester@paymentfunds.local` and create a request.
2. Sign out, sign in as `approver@paymentfunds.local`, and approve it.

Then watch for:

- `Created payment pi_...` in the `dotnet run` console
- `payment_intent.succeeded` forwarded with a `[200]` in the `stripe listen` console
- the request reaching `Completed`, with `Approved By` showing the approver's account

If you create and approve as the same user you will get an access denied page. That is
the separation-of-duties check working, not a bug.

---

## Running the tests

The integration tests hit a real PostgreSQL database rather than the EF InMemory
provider, because the behaviour under test is database behaviour. InMemory does not
enforce unique indexes, so the idempotency tests would pass against a broken app.

One-time setup, with the port-forward running:

```bash
psql -h localhost -p 5432 -U <user> -d PaymentFunds -c 'CREATE DATABASE paymentfunds_test'
```

Plus the `ConnectionStrings:TestDatabase` user secret from step 2 of Getting started.
There is no hardcoded fallback, so an unset value throws with instructions rather than
silently connecting to the wrong database.

Then, from the repository root:

```bash
dotnet test
```

Migrations are applied to the test database on first run, and the tables are
truncated between tests.

`PaymentFundsFactory` boots the real application with four substitutions:

- the connection string points at `paymentfunds_test`
- a known webhook signing secret is injected so signatures can be generated offline
- `IPaymentProcessor` is replaced with a fake, so no call reaches Stripe
- a `TestAuthHandler` replaces cookie authentication, reading the caller's identity and
  roles from `X-Test-User` and `X-Test-Roles` headers

Identity seeding is disabled in tests via `SeedIdentity=false`, and the background
worker is removed with `RemoveAll<IHostedService>()` so it cannot mutate rows
underneath assertions.

`TestAuthHandler` lives only in the test project and is never referenced by the app. A
handler that trusts a request header would be a complete authentication bypass in
production.

Webhook signatures are constructed in the tests using Stripe's own scheme, HMAC-SHA256
over `timestamp.payload`. That replaces manual replay with the Stripe CLI, which is
bounded by a five-minute signature tolerance and sensitive to any reformatting of the
payload bytes.

### What the tests cover

| Fixture | Asserts |
|---|---|
| `StripeWebhookTests` | Forged signatures rejected, valid events transition the request, duplicate event ids recorded once, settlement posts the right ledger entries for both collections and disbursements |
| `PaymentRequestAuthTests` | Anonymous access refused, requesters cannot approve, approvers cannot approve their own request, approvers can approve others', already-approved requests cannot be re-approved |
| `PaymentRequestCreateTests` | Resubmitting the same form creates one row and derives the requester from identity; two separate forms create two rows |
| `PayeeTests` | Only approvers verify, never their own payee, tax form required, disbursements refused to unverified or suspended payees |
| `LedgerTests` | Every transaction balances, unbalanced postings rejected, approval posts and reserves funds, approval refused when short, failures reverse, currencies isolated |

`LedgerTests.Every_transaction_in_the_ledger_balances_to_zero` is the one that matters
most. It asserts across the **whole table** rather than one scenario, so any future code
path that posts an unbalanced set fails it, including paths that don't exist yet. That
matters because Postgres cannot express "sum per `TransactionId` equals zero" as a
check constraint.

Assertions are on database state rather than status codes. `WebApplicationFactory`'s
client follows redirects by default, so a refused action that returns 302 lands on a
200 and would sail past a `StatusCode < 400` assertion. Status codes are only asserted
where the code itself is the point, as with 401, 403, and 400.

> The port-forward must be running or every integration test fails on connection.

---

## Project structure

```
src/PaymentFunds/
  Controllers/
    AccountController.cs           Login, logout, access denied
    HomeController.cs              Dashboard
    LedgerController.cs            Balances, entries, platform funding
    PayeesController.cs            List, create, verify, suspend
    PaymentRequestsController.cs   Create, approve, reject, list, details
    StripeWebhookController.cs     POST /api/stripe/webhook
  Data/
    ApplicationDbContext.cs        IdentityDbContext, DbSets, indexes, constraints
    IdentitySeeder.cs              Roles and development accounts
    LedgerSeeder.cs                System accounts, one set per currency
  Ledger/
    ILedgerService.cs              Posting and balance contract
    LedgerService.cs               Validation, staging, balance queries
    LedgerPosting.cs               LedgerPosting and LedgerLine records
  Extensions/
    DisplayExtensions.cs           Money formatting and status badge classes
  Models/
    PaymentRequest.cs              Core entity
    PaymentStatus.cs               State machine enum
    RequestType.cs                 Collection or Disbursement
    Payee.cs                       Who receives the money
    PayeeStatus.cs                 Unverified, Verified, Suspended
    ProcessedStripeEvent.cs        Webhook deduplication record
    LedgerAccount.cs               A bucket money sits in
    LedgerEntry.cs                 Money moving into or out of one account
    LedgerAccountType.cs           Asset, Liability, Expense, Revenue, Equity
    EntryDirection.cs              Debit or Credit
    ApplicationUser.cs             IdentityUser with DisplayName
    CreatePaymentRequestViewModel.cs
    CreatePayeeViewModel.cs
    DashboardViewModel.cs
    LoginViewModel.cs
  Payments/
    IPaymentProcessor.cs           Provider-agnostic port
    PaymentInstruction.cs          What to pay
    PaymentResult.cs               What happened
    StripePaymentProcessor.cs      The only file that speaks Stripe
  Workers/
    PaymentProcessingWorker.cs     BackgroundService polling loop
  Views/                           Razor views
  Migrations/                      EF Core migrations

tests/PaymentFunds.Tests/
  PaymentFundsFactory.cs           WebApplicationFactory with test overrides
  IntegrationTestBase.cs           Migration and truncation lifecycle
  TestAuthHandler.cs               Header-driven fake authentication
  StripeWebhookTests.cs            Signature, transition, deduplication
  PaymentRequestAuthTests.cs       Roles and separation of duties
  PaymentRequestCreateTests.cs     Idempotency through the form
  PayeeTests.cs                    Verification rules and the disbursement gate
  LedgerTests.cs                   Balance invariant, postings, funds guard, reversal
  PaymentProcessorTests.cs         Fake processor unit tests

k8s/                               PostgreSQL manifests (sops-encrypted secret)
```

---

## Data model

### PaymentRequest

| Column | Type | Notes |
|---|---|---|
| `Id` | int | Primary key |
| `IdempotencyKey` | string | Unique index; forwarded to Stripe |
| `Amount` | decimal | Converted to minor units for Stripe |
| `Currency` | string | Lowercase ISO code (`usd`, `eur`, `gbp`) |
| `Status` | enum | See state machine above |
| `Type` | enum | `Collection` or `Disbursement` |
| `PayeeId` | int? | Required for disbursements, null for collections |
| `RequestedBy` | string | Submitter, from the authenticated user |
| `ApprovedBy` / `ApprovedAt` | string? / DateTime? | Approval audit, from the authenticated user |
| `RejectedBy` / `RejectedAt` | string? / DateTime? | Rejection audit, from the authenticated user |
| `ProviderReference` | string? | Join key for incoming webhooks; provider-neutral by design |
| `CreatedAt` / `ProcessedAt` | DateTime / DateTime? | |

`PayeeId` is nullable because a collection has no payee and because existing rows predate the column. The rule "a disbursement must have a verified payee" can't be expressed as a database constraint, so it lives in application code and is covered by tests.

### Payee

| Column | Type | Notes |
|---|---|---|
| `Id` | int | Primary key |
| `DisplayName` | string | |
| `Email` | string | Unique index |
| `Status` | enum | `Unverified`, `Verified`, `Suspended` |
| `ProviderAccountReference` | string? | Opaque id from the provider, never a bank account number |
| `PayoutDestinationMask` | string? | Display only, for example `••••4321` |
| `TaxFormOnFile` | bool | Required before verification |
| `CreatedBy` / `CreatedAt` | string / DateTime | Creator cannot verify their own payee |
| `VerifiedBy` / `VerifiedAt` | string? / DateTime? | |

The foreign key from `PaymentRequest` uses `DeleteBehavior.Restrict`. Deleting a payee with payment history fails rather than orphaning the audit trail. Retiring one is what `Suspended` is for.

### ProcessedStripeEvent

| Column | Type | Notes |
|---|---|---|
| `Id` | int | Primary key |
| `EventId` | string | Unique index; Stripe's event id |
| `EventType` | string | For example `payment_intent.succeeded` |
| `ProcessedAt` | DateTime | |

### LedgerAccount

| Column | Type | Notes |
|---|---|---|
| `Id` | int | Primary key |
| `Code` | string | `CASH`, `PAYABLE`, `EXPENSE`, `REVENUE`, `OPENING` |
| `Name` | string | Display name |
| `Type` | enum | `Asset`, `Liability`, `Expense`, `Revenue`, `Equity` |
| `Currency` | string | Unique together with `Code`. One currency per account |

### LedgerEntry

| Column | Type | Notes |
|---|---|---|
| `Id` | long | Primary key |
| `TransactionId` | Guid | Groups the entries posted together; indexed |
| `AccountId` | int | Restrict on delete |
| `Direction` | enum | `Debit` or `Credit` |
| `AmountMinor` | long | Minor units, always positive. `CK_LedgerEntry_PositiveAmount` |
| `Currency` | string | Matches the account's currency |
| `PaymentRequestId` | int? | Null for platform funding; indexed; restrict on delete |
| `Description` | string | Human-readable reason |
| `CreatedAt` | DateTime | |

Entries are append-only. Nothing updates or deletes them; corrections are posted as
reversing entries. Both foreign keys are `Restrict`, so an account or request with
entries cannot be deleted.

The balanced-per-transaction invariant is enforced in `LedgerService` and asserted
across the whole table by `LedgerTests`, because Postgres cannot express it as a simple
check constraint.

### Identity

The standard ASP.NET Core Identity tables (`AspNetUsers`, `AspNetRoles`,
`AspNetUserRoles`, and the rest) live in the same database and context.
`ApplicationUser` extends `IdentityUser` with a `DisplayName`.

Audit columns store the user's name as a string rather than a foreign key to
`AspNetUsers`. That keeps the audit trail intact if an account is ever deleted, at the
cost of no referential integrity.

---

## Current limitations

Documented deliberately rather than hidden.

- **Even a disbursement settles through a `PaymentIntent`.** The domain now models disbursement correctly, but the Stripe call underneath still creates a charge. Real payouts use `Payout` or `Transfer` with Connect, which requires recipient onboarding and compliance handling that test mode doesn't provide.
- **Rows can strand in `Processing`.** The worker catches `StripeException` but not other failures. A crash after claiming a row leaves it claimed with nothing to retry it. There is no attempt counter, backoff, dead letter, or lease expiry. A sweeper is planned.
- **No per-payee subledger.** One `PAYABLE` account per currency holds every obligation. Per-payee balances are derivable by joining through `LedgerEntry.PaymentRequestId`, but there is no dedicated account per payee.
- **No currency conversion.** Accounts are per-currency and a posting cannot span currencies, which is correct but means cross-currency movement is impossible rather than handled.
- **No reconciliation against Stripe.** Nothing compares the ledger to the provider's record of the same payments, so undetected drift is possible.
- **No approval thresholds.** Every request needs exactly one approver regardless of amount. Dual approval over a limit is planned.
- **Coverage is uneven.** The webhook boundary, the authorization rules, payee verification, form idempotency, and the ledger are covered. The worker's polling, claiming, payee re-check, and reversal are not, because the factory removes hosted services during tests. `LedgerTests` reproduces the worker's reversal rather than invoking it, so a change to the worker alone would not fail a test.
- **`StripeConfiguration.ApiKey` is a global static.** `StripePaymentProcessor` reads it implicitly rather than receiving injected configuration.
- **No browser tests.** Coverage is HTTP-level through `WebApplicationFactory`. Playwright is planned.
- **The app is not containerized.** Only PostgreSQL runs in Kubernetes. The app runs on the host and reaches the database through `kubectl port-forward`.

---

## Roadmap

| Phase | Status |
|---|---|
| 0. Project foundations | Complete |
| 1. Domain model and database | Complete |
| 2. Create and approve requests | Complete |
| 3. Idempotency | Complete |
| 4. Async processing | Complete |
| 5. Webhooks | Complete |
| 6. Test harness and the Stripe seam | Complete |
| 7. Identity and roles | Complete |
| 8. Payees and disbursement modeling | Complete |
| 8.5. Interface polish | Complete |
| 9. Double-entry ledger | Complete |
| 10. Containerization, observability, Kubernetes deployment | Planned |

Deferred: approval policy engine (thresholds, dual approval) and worker resilience
(retry, backoff, dead-letter, stuck-row sweeper).
