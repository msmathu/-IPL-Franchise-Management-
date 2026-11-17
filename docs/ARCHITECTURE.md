# Architecture

## Modular monolith

```
Ipl.Api ──► Ipl.Infrastructure ──► Ipl.Application ──► Ipl.Domain
 (HTTP, auth,     (EF Core, Dapper,      (business modules,      (entities, rules,
  SignalR,         cache, e-mail,         DTOs, validation,       permissions,
  middleware)      exports, jobs)         workflows)              no dependencies)
```

- **Domain:** entities, enums, the permission and role catalog (`Authorization.cs`), season rule keys (`RuleKeys.cs`) and pure rules (bid increments, trade approval order).
- **Application:** one folder per module: `Identity`, `Franchises`, `Players`, `Statistics`, `Analytics` (rating, valuation, comparison, recommendations), `Trading`, `Squads` (squad, retention, Playing XI), `Auctions`, `Scouting`, `Finance`, `Operations` (medical, messaging, notifications, audit, settings) and `Reports`. Services depend on interfaces (`IAppDbContext`, `ICurrentUser`, `IAuditService`, `INotificationService`, `IRealtimePublisher`, `IAppCache`, `IReportQueries`), so a module can be moved behind its own API later.
- **Infrastructure:** `AppDbContext` (SQL Server), migrations, Dapper report queries, Redis or in-memory cache, e-mail outbox, QuestPDF/ClosedXML exports, seeders and background jobs.
- **API:** thin controllers (`/api/v1/...`), JWT and permission policies, response envelope, exception mapping, rate limiting, security headers, SignalR hub, static hosting of the Angular app.

### Request flow

1. The JWT is validated, then the user's **security stamp** is checked (cached for 30 s), so deactivated users and changed roles lose access within seconds.
2. A `[HasPermission]` policy checks the endpoint; the service checks **again** (`me.Require(...)`) and checks the franchise scope (`RequireFranchise`, `RequireFranchiseAccess`).
3. EF Core global **query filters** hide confidential rows of other franchises: Playing XIs, retention plans, contracts, finance, budgets, scouting, comparisons, medical records and documents. Message threads are visible only to their franchises; notifications only to their owner.
4. Money and squad changes run inside **database transactions** (serializable for trade completion, retention submission and auction hammer; `UPDLOCK` row lock for bids). Unique constraints and idempotency keys prevent duplicates.
5. Sensitive operations write an **audit log** row in the same unit of work.
6. Real-time updates go out through **SignalR** after the commit.

### Real-time

One hub, `/hubs/live`. On connect, users join `user:{id}` and `franchise:{id}` groups. Auction rooms are joined explicitly (needs `auctions.read`). Clients never change state through the hub: bids go through `POST /api/v1/auctions/{id}/bids`, which validates them and then broadcasts.

### Background services

- `ScheduledJobs` (every 5 min): expires unanswered trade offers, sends payment-due reminders and retention-deadline reminders (7/3/1 days). Each reminder is sent only once (dedup key).
- `EmailDispatcher`: sends the e-mail outbox when SMTP is configured.

## Database design (schema `ipl`)

| Group | Tables |
| --- | --- |
| Identity | Users, Roles, Permissions, RolePermissions, UserRoles, RefreshTokens, LoginHistory, UserTokens, EmailMessages, AuditLogs, DataProtectionKeys |
| League | Seasons, SeasonRules, Franchises, FranchiseSeasons (results, purse), FranchiseStaff, FranchiseMembers, SystemSettings |
| Players | Countries, PlayerRoles, Players, PlayerTeams, PlayerAvailability |
| Statistics | Competitions, Matches, PlayerMatchStatistics, PlayerSeasonStatistics, PlayerCareerStatistics |
| Trading | TradeOffers, TradeOfferPlayers, TradeNegotiations, TradeApprovals, TradeTransactions |
| Squad | FranchiseSquads, PlayingXIs, PlayingXIPlayers, RetentionPlans, RetentionRecords, ReleaseRecords |
| Auction | AuctionEvents, AuctionPlayers, AuctionBids, AuctionResults |
| Finance | PlayerContracts, ContractPayments, FinanceTransactions, BudgetAllocations |
| Intelligence | ScoutingAssignments, PlayerScoutingReports, PlayerValuations, PlayerComparisons |
| Operations | PlayerMedicalRecords, MessageThreads, Messages, MessageThreadReads, Notifications, NotificationPreferences, Documents |

Key constraints:

- `FranchiseSquads (SeasonId, PlayerId) UNIQUE WHERE Status = 'Active'`: a player is in at most one active squad per season.
- `TradeTransactions.TradeOfferId UNIQUE`: a trade is completed once.
- `FinanceTransactions.IdempotencyKey UNIQUE`: the same money movement is never booked twice.
- `AuctionBids (FranchiseId, IdempotencyKey) UNIQUE`; `AuctionResults (AuctionPlayerId, Round) UNIQUE`.
- `SeasonRules (SeasonId, Key) UNIQUE`; `RefreshTokens.TokenHash UNIQUE`; `Notifications (UserId, DedupKey) UNIQUE`.
- Row versions (`rowversion`) on franchises, franchise seasons (purse), players, trade offers, auction events and lots.
- Soft delete (global filter) for franchises, players, scouting reports and documents.
- Relationships are `RESTRICT` unless a child collection is owned (trade players/negotiations/approvals, payments, XI players, messages).
- Enums are stored as strings; money is `decimal(18,2)` in rupees.

### Purse vs salary vs trade consideration

- **Auction purse** (`FranchiseSeason.PurseRemaining`) changes only through `FinanceTransactions` in the `AuctionPurse` category (auction purchases, retention costs, and trade salary/consideration *only if the season rules say so*).
- **Player compensation** (contract salaries and payments) and **trade consideration** are separate ledgers.
- The trade impact calculator shows all three separately and states which season rules applied.

## Rating and valuation models

- **Rating (0–100):** weighted factors (recent form, career, age profile, consistency, competition level, public availability, development potential). The weights are in *Settings → Rating model*. Each factor returns its score and a plain-language explanation. Only recorded statistics are used, never medical details.
- **Valuation:** rating mapped to a share of the purse, blended with the last known price and the average price for the role in recorded auctions, adjusted for demand (role scarcity), age and availability; the range width depends on data confidence. Results are rounded to ₹5 lakh, labelled *model estimate*, and stored with their explanation (`PlayerValuations`).
