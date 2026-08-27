# PaymentFunds

An ASP.NET Core MVC application for submitting payment requests, routing them through an approval gate, and processing approved requests asynchronously against the Stripe API.

Built to work through the patterns real payment systems depend on: explicit state machines, idempotency at every retry boundary, asynchronous processing that survives restarts, and authenticated webhooks.

> **Test mode only.** This app runs against Stripe's test environment and has no authentication. See [Current limitations](#current-limitations).

---

## Table of contents

- [How it works](#how-it-works)
- [Tech stack](#tech-stack)
- [Architecture](#architecture)
- [Key design decisions](#key-design-decisions)
- [Getting started](#getting-started)
- [Project structure](#project-structure)
- [Data model](#data-model)
- [Testing the flow](#testing-the-flow)
- [Current limitations](#current-limitations)
- [Roadmap](#roadmap)

---

## How it works

A payment request moves through an explicit state machine. Nothing skips a step, and every transition is guarded server-side.

```
                 ┌──────────────────┐
                 │ PendingApproval  │  ← submitted via web form
                 └────────┬─────────┘
                   approve│reject
              ┌───────────┴───────────┐
              ▼                       ▼
        ┌──────────┐            ┌──────────┐
        │ Approved │            │ Rejected │  (terminal)
        └────┬─────┘            └──────────┘
             │ background worker claims the row
             ▼
       ┌────────────┐
       │ Processing │  ← Stripe PaymentIntent created
       └─────┬──────┘
     webhook │ from Stripe
      ┌──────┴───────┐
      ▼              ▼
┌───────────┐  ┌──────────┐
│ Completed │  │  Failed  │
└───────────┘  └──────────┘
```

1. **Submit.** A user fills in amount, currency, and requester. The server mints an idempotency key when it renders the form, so resubmitting the same form cannot create a duplicate.
2. **Approve.** An approver approves or rejects. Both actions are recorded with a name and timestamp. Only `PendingApproval` requests can be acted on.
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
| Payments | Stripe (Stripe.net 52.x) |
| Async processing | `BackgroundService` polling the database |
| Orchestration | Kubernetes (minikube locally) |
| Secrets (dev) | .NET User Secrets |

---

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [minikube](https://minikube.sigs.k8s.io/docs/start) and `kubectl`
- [Stripe CLI](https://docs.stripe.com/cli)
- A Stripe account (test mode)