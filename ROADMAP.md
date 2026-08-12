# PaymentFunds Roadmap

ASP.NET Core MVC payment/disbursement app. Built in phases, pausing at each to explain concepts along the way.

**Stack decisions**
- Database: PostgreSQL (EF Core + Npgsql provider)
- Payment processor: Stripe (Stripe.net SDK)
- Pattern: MVC
- Async processing: background worker pulling from a queue
- Containerized with Docker

## Phase 0: Foundations (done)
Scaffolded MVC project, running locally with `dotnet run`. Clean up placeholder `Product.cs` model and default views.

## Phase 1: Domain model and database
Define `PaymentRequest` entity (amount, currency, status, idempotency key, timestamps, requester info). Add EF Core with the Npgsql provider, running PostgreSQL in Docker. Model the state machine explicitly:
Created -> PendingApproval -> Approved/Rejected -> Processing -> Completed/Failed.

## Phase 2: Create and approve requests (MVC layer)
Controllers and views for submitting a payment request and for an approver to review, approve, or reject it.

## Phase 3: Idempotency
Client sends an idempotency key with each request. Server checks the database before creating a new record; if the key was already used, return the original result instead of duplicating it.

## Phase 4: Async processing via queue
Approved requests are pushed onto a queue instead of processed inline. A background worker (`BackgroundService`) pulls from the queue and calls the Stripe API (e.g. create a PaymentIntent) to actually move money. Decouples the web request from the slow part.

## Phase 5: Webhooks
Endpoint to receive status callbacks from Stripe (payment succeeded, failed, etc). Verify authenticity using Stripe's webhook signature verification (`Stripe-Signature` header) before trusting the payload.

## Phase 6: Testing
Unit tests for domain logic and idempotency handling. API/integration tests using `WebApplicationFactory`. Browser tests (Playwright) for the create/approve flow end to end.

## Phase 7: Dockerize
Dockerfile for the app, docker-compose for app + PostgreSQL + queue. Goal: `docker compose up` gets a working app from a clean machine.

## Phase 8: Hardening
Logging, structured error handling, basic observability.
