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

1. **Submit.** A signed-in user fills in amount and currency. The requester is taken from their authenticated identity, never from form input. The server mints an idempotency key when it renders the form, so resubmitting the same form cannot create a duplicate.
2. **Approve.** A user in the `Approver` role approves or rejects, and cannot act on their own request. Both actions are recorded against the authenticated user with a timestamp. Only `PendingApproval` requests can be acted on.
3. **Process.** A background worker polls for `Approved` rows every five seconds, claims one atomically, and calls Stripe to create a PaymentIntent. The request's idempotency key is forwarded to Stripe so a retry cannot double-charge.
4. **Settle.** Stripe calls back over a webhook when the payment resolves. The signature is verified, the event is deduplicated, and the request moves to `Completed` or `Failed`.

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

They coordinate entirely through rows in Postgres. The `Status` column is the message: setting `Approved` posts a job, and the worker flipping it to `Processing` claims it. `StripePaymentIntentId` is the join key an inbound webhook uses to find the request it refers to.

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
| `StripeWebhookTests` | Forged signatures are rejected, valid events transition the request, duplicate event ids are recorded once |
| `PaymentRequestAuthTests` | Anonymous access refused, requesters cannot approve, approvers cannot approve their own request, approvers can approve others', already-approved requests cannot be re-approved |
| `PaymentRequestCreateTests` | Resubmitting the same form creates one row and derives the requester from identity; two separate forms create two rows |

> The port-forward must be running or every integration test fails on connection.

---

## Project structure

```
src/PaymentFunds/
  Controllers/
    AccountController.cs           Login, logout, access denied
    PaymentRequestsController.cs   Create, approve, reject, list, details
    StripeWebhookController.cs     POST /api/stripe/webhook
  Data/
    ApplicationDbContext.cs        IdentityDbContext, DbSets, unique indexes
    IdentitySeeder.cs              Roles and development accounts
  Models/
    PaymentRequest.cs              Core entity
    PaymentStatus.cs               State machine enum
    ProcessedStripeEvent.cs        Webhook deduplication ledger
    ApplicationUser.cs             IdentityUser with DisplayName
    CreatePaymentRequestViewModel.cs
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
| `RequestedBy` | string | Submitter, from the authenticated user |
| `ApprovedBy` / `ApprovedAt` | string? / DateTime? | Approval audit, from the authenticated user |
| `RejectedBy` / `RejectedAt` | string? / DateTime? | Rejection audit, from the authenticated user |
| `StripePaymentIntentId` | string? | Join key for incoming webhooks |
| `CreatedAt` / `ProcessedAt` | DateTime / DateTime? | |

### ProcessedStripeEvent

| Column | Type | Notes |
|---|---|---|
| `Id` | int | Primary key |
| `EventId` | string | Unique index; Stripe's event id |
| `EventType` | string | For example `payment_intent.succeeded` |
| `ProcessedAt` | DateTime | |

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

- **`PaymentIntent` is a charge, not a disbursement.** Stripe's `PaymentIntent` collects money. Real disbursement uses `Payout` or `Transfer` with Connect, which requires recipient onboarding and compliance handling.
- **Rows can strand in `Processing`.** The worker catches `StripeException` but not other failures. A crash after claiming a row leaves it claimed with nothing to retry it. There is no attempt counter, backoff, dead letter, or lease expiry. A sweeper is planned.
- **No payee model.** A request records who asked for money, not who receives it. There is no payout destination, tax identity, or verification state.
- **No ledger or balance.** Nothing tracks whether funds are available, so insufficient funds is not a reachable state.
- **No approval thresholds.** Every request needs exactly one approver regardless of amount. Dual approval over a limit is planned.
- **Coverage is uneven.** The webhook boundary, the authorization rules, and form idempotency are covered. The worker's polling and claiming logic is not.
- **`StripeConfiguration.ApiKey` is a global static.** `StripePaymentProcessor` reads it implicitly rather than receiving injected configuration.
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
| 8. Payees and disbursement modeling | Next |
| 9. Double-entry ledger | Planned |
| 10. Observability and Kubernetes deployment | Planned |

Deferred: approval policy engine (thresholds, dual approval) and worker resilience
(retry, backoff, dead-letter, stuck-row sweeper).
