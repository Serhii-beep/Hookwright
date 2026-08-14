# Hookwright

[![CI](https://github.com/Serhii-beep/Hookwright/actions/workflows/ci.yml/badge.svg)](https://github.com/Serhii-beep/Hookwright/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

**Status:** early development. Not yet published to NuGet.

---

## The pitch

Every SaaS that grows past its first few integrations has to build outbound webhooks, and every one
of them rebuilds the same thing: a queue, a retry ladder, HMAC signing, a delivery log, and a
support-ticket-shaped hole where a self-serve UI should be. The existing answers — Svix, Convoy,
Hookdeck Outpost, Hook0 — are all *separate services*: a Rust or Go binary plus Postgres plus Redis
plus a message broker plus monitoring plus upgrades plus on-call, bolted next to an app that already
has a web server and a database. Hookwright is the same capability delivered as a library that runs
inside the ASP.NET Core app you already deploy, storing state in the database you already run. One
`dotnet add package`, one `AddHookwright()`, and — because the event is written in *your* transaction —
a delivery guarantee no external service can offer.

## Why not just use Svix or Convoy?

Often you should — they are mature products with companies behind them. Hookwright exists for three
reasons they cannot address:

1. **Nobody ships this shape.** Every alternative is an out-of-process service with its own
   datastores to operate. Hookwright is library-first, with standalone-service mode as an escape
   hatch rather than the entry fee.
2. **.NET has no answer at all.** `aspnet/WebHooks` was archived in October 2018 and
   `Microsoft.AspNetCore.WebHooks.Receivers` was deprecated while still at `1.0.0-preview2-final`.
   A .NET team's options today are "run a Go service" or "write it yourself".
3. **Embedding is strictly more correct, not a compromise.** With any out-of-process vendor you
   write `db.SaveChanges()` then `vendor.Send()`. Crash between them and the event is lost forever;
   reverse the order and a rolled-back transaction emits a phantom event. The standard fix is a
   transactional outbox — which is a table in *your* database. Hookwright is that table plus the
   delivery engine that drains it.

## What it does

- **Publish** — `webhooks.Enqueue("order.created", payload)` enlists in your existing `DbContext`.
  The event is committed atomically with your business write, or not at all.
- **Deliver** — leased, at-least-once delivery with exponential backoff + full jitter, per-endpoint
  circuit breaking, adaptive concurrency, `Retry-After` and `429`/`410` handling, and opt-in
  ordered delivery partitioned by a key you choose.
- **Sign** — [Standard Webhooks](https://www.standardwebhooks.com/) compliant out of the box
  (`webhook-id` / `webhook-timestamp` / `webhook-signature`, HMAC-SHA256, `whsec_` secrets), so your
  consumers can verify with any existing library in any language.
- **Expose** — an embeddable self-serve portal your customers use to register endpoints, inspect
  every request and response, and retry failures — without opening a support ticket, and without
  knowing Hookwright exists.
- **Recover** — a cursor API over the durable event log (`GET /events?after=evt_…`). If a consumer
  is down past the retry window, they replay the backlog instead of losing it.
- **Receive** — a consumer-side package whose verification middleware reads the raw body *before*
  model binding, eliminating the most common webhook bug in production.

## Requirements

- .NET 10 or later
- PostgreSQL, SQL Server, or SQLite
- No other infrastructure: no Redis, no message broker, no sidecar

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

MIT — see [LICENSE](LICENSE).
