# Franchise HQ — Cricket Franchise Management Platform

A franchise management platform for T20 franchise cricket. Owners, CEOs, cricket directors, coaches, scouts, analysts, auction managers, finance, medical and operations teams use it to run a franchise through a whole season: player research, statistics, comparison, scouting, valuation, retention and releases, player trading, live auctions, squad and Playing XI planning, contracts and finance, communication and reporting.

> **Independent software.** It is not affiliated with, endorsed by or connected to the IPL, BCCI or any franchise. It ships with **no official logos, branding or proprietary data**. The included demo league is entirely fictional (see [Data & licensing](docs/DATA.md)).

## What's inside

| Area | Highlights |
| --- | --- |
| **Access** | JWT access tokens (15 min) and rotating HttpOnly refresh cookies with reuse detection; lockout; forgot/reset password; invitations; activation/deactivation; sessions; login history; **15 roles** and **46 permissions**, checked on the server for every request; per-franchise data isolation |
| **Franchises** | Profiles, owner and management, coaching staff, platform users, season results, championships, finances, activity history |
| **Players** | Database with the search/filter set from the spec (country, role, category, style, age, team, price, performance, availability, status), CSV import, profiles, franchise history, public availability |
| **Statistics** | Franchise, domestic and international records; season-by-season and career; last 5/10/20, form trend, year-wise, home/away, opponents, venues, powerplay/middle/death, pace vs spin, boundary %, dot-ball %, dismissal patterns, wickets by batting hand. **Every dataset shows its source and last-updated date.** |
| **Comparison** | 2–5 players: radar and bar charts, comparison table, saved and shared reports |
| **Trading** | Cash trades, swaps, multi-player and players-plus-cash; counteroffers, expiry, messages and internal notes; approvals in order (franchise authorisation → player consent → league approval); impact calculator that keeps **salary, trade consideration and auction purse separate**; completion in one serializable transaction |
| **Retention** | Simulations against configurable limits and slot costs, saved plans, one-time final submission (purse debit and automatic release), individual releases, history |
| **Auction** | Configuration, player pool and sets, base prices, configurable bid increments, live bidding over SignalR, server-side validation (purse with minimum-squad reserve, squad size, overseas limit, increments, eligibility, row locks, idempotency keys), sold/unsold, reopening unsold players, results and exports |
| **Squad & XI** | Composition, strength, availability; Playing XI builder with captain, vice-captain, keeper, bowling options and Impact Player substitutes, checked against season rules |
| **Scouting** | Assignments, reports with ratings and recommendations, reviews, confidential documents, emerging-talent discovery |
| **Valuation** | Explainable rating model (configurable weights) and valuation model, always labelled as *model estimates*; undervalued/overvalued players, auction strategy, squad needs, trade value |
| **Finance** | Separate ledgers (auction purse, player compensation, trade consideration, other expenses); budgets; contracts with payment schedules; approval threshold; immutable, idempotent transactions |
| **Operations** | Messages (internal, franchise-to-franchise, trade), real-time notifications with preferences, medical records (medical staff only) with public availability, role dashboards, PDF/Excel/CSV exports, audit logs |

**Season rules are data, not code.** Purse, squad limits, overseas limits, Impact Player, retention limits and costs, bid increments, trade window and approval rules are configured per season in *Settings → Seasons & rules*.

## Technology

- **Backend:** ASP.NET Core (.NET 10) Web API, C#, Entity Framework Core 10 (SQL Server), Dapper for reporting queries, SignalR, background services, Asp.Versioning (`/api/v1`), Swagger/OpenAPI, Serilog structured logging, built-in rate limiting, QuestPDF and ClosedXML exports
- **Database:** Microsoft SQL Server (2019+), schema `ipl`, EF Core migrations, read-committed snapshot isolation
- **Cache:** Redis (optional; falls back to in-memory)
- **Frontend:** Angular 22 (standalone components, signals, zoneless), Angular Material, Reactive Forms, lazy-loaded routes, Chart.js, @microsoft/signalr
- **Packaging:** Dockerfile and Docker Compose (SQL Server and Redis)

Architecture: modular monolith (Domain → Application → Infrastructure → API), with each business module in its own folder so it can be extracted later. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Quick start (Windows, local SQL Server)

Requirements: .NET 10 SDK, Node.js 22+, SQL Server 2019+ (the connection string assumes the instance `.\MSSQLSERVERSE` with Windows authentication; change it in `backend/src/Ipl.Api/appsettings.Development.json`).

```bash
# 1. API (creates the database, applies migrations and seeds the demo league on first start)
cd backend/src/Ipl.Api
dotnet user-secrets set "Jwt:SigningKey" "<48+ random characters>"
dotnet run --launch-profile http                 # http://localhost:5080  (Swagger: /swagger)

# 2. Web app (dev server with proxy to the API)
cd frontend
npm install
npx ng serve                                     # http://localhost:4200
```

Or serve both from the API: `npx ng build` in `frontend`, copy `frontend/dist/web/browser` to `backend/src/Ipl.Api/wwwroot`, then open http://localhost:5080.

### Docker

```bash
cp .env.example .env      # set SQL_SA_PASSWORD and JWT_SIGNING_KEY
docker compose up --build # http://localhost:8080
```

### Demo accounts (development data only)

Password for every demo account: `Demo@2026pass`.

| User | Role |
| --- | --- |
| `admin@demo.local` | Platform Super Admin |
| `league@demo.local` | League Administrator |
| `owner.mum@demo.local` | Franchise Owner (Mumbai Mariners) |
| `ceo.mum`, `cricket.director.mum`, `head.coach.mum`, `assistant.coach.mum`, `auction.manager.mum`, `scouting.manager.mum`, `scout.mum`, `analyst.mum`, `finance.manager.mum`, `medical.staff.mum`, `team.operations.mum`, `viewer.mum` (all `@demo.local`) | One of each franchise role at Mumbai Mariners |
| `owner.chn`, `ceo.chn`, `cricket.director.chn`, `auction.manager.chn`, `team.operations.chn` (`@demo.local`) | Chennai Cheetahs (for trades between two franchises) |
| `owner.<code>`, `auction.<code>` (`blr`, `kol`, `del`, `hyd`, `jai`, `pun`, `lko`, `ahm`) | Owner and auction manager of the other franchises |

Turn the demo data off with `DemoData:Enabled=false` (the default outside Development).

## Documentation

- [Architecture & database design](docs/ARCHITECTURE.md)
- [API reference](docs/API.md) (and Swagger at `/swagger`)
- [Configuration, deployment, backup & recovery](docs/OPERATIONS.md)
- [Security, audit & compliance](docs/SECURITY.md)
- [Data sources, licensing & CSV import](docs/DATA.md)
- [Testing](docs/TESTING.md)

## Repository layout

```
backend/
  IplFranchise.sln
  src/Ipl.Domain          entities, enums, permission & role catalog, rule keys
  src/Ipl.Application     business modules (Identity, Franchises, Players, Statistics, Analytics, Trading,
                          Squads/Retention, Auctions, Scouting, Finance, Operations, Reports)
  src/Ipl.Infrastructure  EF Core DbContext + migrations, Dapper reports, cache, e-mail, exports, seeders, jobs
  src/Ipl.Api             controllers (/api/v1), auth, SignalR hub, middleware, Program.cs
  tests/Ipl.Tests         unit tests + integration tests against a throw-away SQL Server database
frontend/                 Angular app (core/, shared/, layout/, features/<module>/)
Dockerfile, docker-compose.yml, .env.example
```
