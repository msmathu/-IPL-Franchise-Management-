# API reference (v1)

Base path: `/api/v1`. Interactive documentation is at **`/swagger`** (enabled in Development, or with `Swagger:Enabled=true`).

## Conventions

- **Auth:** `Authorization: Bearer <access token>` from `POST /auth/login`. The refresh token is an HttpOnly cookie (`ipl_refresh`, path `/api/v1/auth`); call `POST /auth/refresh` to rotate it.
- **Responses:** `{ "success": true, "data": … }` or `{ "success": false, "error": { "code", "message", "field", "traceId" } }`.
- **Status codes:** 400 validation · 401 not signed in · 403 missing permission or another franchise's data · 404 not found (also used to hide other franchises' confidential records) · 409 conflict/duplicate/concurrency · 422 business rule (e.g. `auction.bid_rejected`, `trade.window`, `finance.insufficient_purse`) · 429 rate limited.
- **Paging:** `?page=1&pageSize=25&sort=name&desc=false` → `{ items, page, pageSize, total, totalPages }`.
- **Money:** rupees as decimals. **Dates:** ISO-8601 (UTC for timestamps).
- **Idempotency:** bids (`idempotencyKey`) and manual expenses (`idempotencyKey`) can be retried safely.
- **Franchise scope:** franchise users act for their active franchise (switch with `POST /auth/switch-franchise`). League-wide users can pass `franchiseId` where an endpoint accepts it.

## Endpoints

### Auth `/auth`
`POST login` · `POST refresh` · `POST logout` · `POST forgot-password` · `POST reset-password` · `POST accept-invitation` · `GET me` · `POST change-password` · `POST switch-franchise` · `GET sessions` · `DELETE sessions/{id}` · `GET login-history`

### Users `/users` (users.manage / users.admin)
`GET` (search, paging) · `GET roles` · `GET permissions` · `POST invite` · `POST {id}/activate` · `POST {id}/deactivate` · `PUT {id}/roles` · `GET {id}/login-history`

### Franchises `/franchises`
`GET` · `GET {id}` · `GET {id}/squad` · `GET {id}/activity` · `POST` (franchises.manage) · `PUT {id}` · `POST {id}/activate|deactivate` · `POST {id}/staff` · `DELETE {id}/staff/{staffId}`

### Players `/players`
`GET lookups` · `GET` (filters: `search, countryId, category, battingHand, bowlingType, ageMin, ageMax, franchiseId, overseas, capped, status, availability, priceMin, priceMax, minRuns, minWickets, minStrikeRate, maxEconomy, freeAgentsOnly`) · `GET {id}` · `POST` · `PUT {id}` · `DELETE {id}` · `POST import` (CSV) · `PUT {id}/availability` · `GET {id}/statistics` · `GET {id}/analysis?competitionId=` · `GET {id}/rating` · `GET {id}/valuation` · `POST {id}/valuation`

### Statistics `/statistics`
`POST import` (season CSV, stats.import) · `POST recalculate`

### Comparisons `/comparisons`
`POST compare` `{ playerIds: [2–5] }` · `GET` (saved) · `POST` (save) · `DELETE {id}`

### Valuation `/valuation`
`GET strategy` · `GET undervalued` · `GET overvalued` · `GET squad-needs`

### Trades `/trades`
`GET ?status=open|closed|<Status>` · `GET {id}` · `POST` `{ terms: { counterpartyFranchiseId, playersOffered, playersRequested, cashConsideration, cashPayerFranchiseId, conditions }, submit, message }` · `PUT {id}` (draft) · `POST {id}/submit` · `POST {id}/counter` · `POST {id}/accept` · `POST {id}/reject` · `POST {id}/withdraw` · `POST {id}/messages` `{ message, internal }` · `POST {id}/approvals/{approvalId}` `{ approve, comment }` · `POST {id}/complete` · `GET {id}/impact` · `POST impact` (preview) · `POST value`

Statuses: Draft → PendingResponse ⇄ CounterofferReceived → AcceptedInPrinciple → AwaitingPlayerConsent → AwaitingLeagueApproval → Approved → Completed (or Rejected / Withdrawn / Expired).

### Retention `/retention`
`GET` (overview) · `POST simulate` · `POST plans` · `DELETE plans/{id}` · `POST plans/{id}/submit` · `POST release` · `GET history`

### Squad `/squad`
`GET` · `GET playing-xi` · `POST playing-xi/validate` · `POST playing-xi` · `PUT playing-xi/{id}` · `DELETE playing-xi/{id}`

### Auctions `/auctions`
`GET` · `POST` · `PUT {id}` · `GET {id}/lots` · `POST {id}/lots` · `POST {id}/lots/import-eligible` · `DELETE {id}/lots/{lotId}` · `POST {id}/ready|start|pause|resume|complete` · `POST {id}/open-lot` · `POST {id}/bids` `{ lotId, amount, idempotencyKey }` · `POST {id}/close-lot` `{ sold }` · `POST {id}/reopen-unsold` · `GET {id}/live` · `GET {id}/results`

SignalR `/hubs/live`: invoke `JoinAuction(id)`; events `auction` (`{ kind: status|lotOpened|bid|sold|unsold, payload }`), `notification`, `trade.updated`, `message`.

### Scouting `/scouting`
`GET scouts` · `GET assignments` · `POST assignments` · `PUT assignments/{id}` · `GET reports` · `GET reports/{id}` · `POST reports` · `PUT reports/{id}` · `POST reports/{id}/review` · `DELETE reports/{id}` · `POST reports/{id}/documents` · `GET reports/{id}/documents/{docId}` · `GET emerging?maxAge=&category=&freeAgentsOnly=`

### Finance `/finance`
`GET overview` · `GET transactions?category=` · `POST expenses` · `PUT budgets` · `GET contracts` · `POST contracts` · `PUT contracts/{id}` · `POST contracts/{id}/terminate` · `POST payments/{id}/pay`

### Communication
`/messages`: `GET` · `GET unread` · `GET {id}` · `POST` (new thread) · `POST {id}` (reply)
`/notifications`: `GET` · `GET unread-count` · `POST {id}/read` · `POST read-all` · `GET preferences` · `PUT preferences`

### Medical `/medical`
`GET availability` · `GET records` (medical.read) · `POST records` · `PUT records/{id}` · `POST records/{id}/clear`

### Reports `/reports`
`GET dashboard` · `GET types` · `GET {type}/preview` · `GET {type}/export?format=pdf|xlsx|csv&id=` (types: `squad, auction-results, finance-ledger, contracts, trades, player-stats`)

### Audit & settings
`/audit`: `GET ?action=&entityType=&user=&from=&to=&franchiseId=`
`/settings`: `GET seasons` · `POST seasons` · `POST seasons/{id}/current` · `GET seasons/{id}/rules` · `PUT seasons/{id}/rules/{key}` · `GET rating-weights` · `PUT rating-weights`

Health: `GET /health` (no auth).
