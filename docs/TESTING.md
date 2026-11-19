# Testing

## Automated tests (`backend/tests/Ipl.Tests`)

```bash
cd backend
dotnet test
```

- **Unit tests** cover the pure rules: bid increment tiers, retention limits and slot costs, trade approval order, valuation ranges and rounding, team strength with the overseas limit and keeper penalty, season-rule validation, statistics aggregation, form trend, CSV parsing and formula-safe CSV export.
- **Integration tests** start the real API in memory (`WebApplicationFactory`) against a **throw-away SQL Server database** that is created, seeded with a small demo league and dropped. Set `IPL_TEST_SQL` to use a server other than `.\MSSQLSERVERSE`. They cover:
  - login, lockout, refresh rotation and token-reuse detection, unauthenticated access
  - permission checks and tenant isolation (medical data, other franchises' finances, internal trade notes, private drafts)
  - privilege-escalation protection when inviting users
  - the full trade lifecycle (invalid offer, impact preview, counteroffer, approvals in order, league completion, idempotent completion, squad, ledger and purse effects), plus reject and withdraw
  - a live auction (wrong amounts, idempotent retries, a two-franchise bid race with exactly one winner, sale booking, unsold, reopen, complete, SignalR events)
  - retention submission (costs, single submission, release of the rest), Playing XI rule validation, finance approval thresholds and duplicates
  - PDF, Excel and CSV exports and dashboard sections; data provenance in statistics
  - injury replacements (not injured → refused, wrong role → forbidden, league approval swaps the players once), season centre and records endpoints
- **Cricsheet unit tests:** scorecard name matching (initials, spelling variants, ambiguous names left unlinked) and a hand-written match checking batting, bowling, extras, fall of wickets, catches and batter-v-bowler counts.

**Current status:** all 55 tests pass (30 s). They cover the original workflows plus Cricsheet name matching and scorecards (unit), injury replacements with league approval, and the season-centre/records endpoints (integration). Two long-standing bugs found by these tests were fixed: submitting retentions and accepting trades failed with a false "changed at the same time" error (entity ids are client-generated, so new child rows must be inserted, not updated).

## Manual / browser checks

The web app was checked page by page in headless Chrome as the owner, cricket director, head coach, medical staff and league administrator. Every page loads without console errors or failed API calls, and roles without access are sent to the "no access" page.

## Things to check before going live

- Configure the official season rules.
- Load licensed data.
- Configure SMTP.
- Turn off demo data.
- Run a backup and restore test (see [OPERATIONS.md](OPERATIONS.md)).
