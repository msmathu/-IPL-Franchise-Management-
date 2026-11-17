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

**Current status:** the tests were written against the earlier fictional league (seeded trade, synthetic statistics and prices for every player). After switching to real squads, several integration tests need updating (account names were updated; scenarios that relied on seeded trades, prices or statistics for squad players were not). Unit tests are unaffected.

## Manual / browser checks

The web app was checked page by page in headless Chrome as the owner, cricket director, head coach, medical staff and league administrator. Every page loads without console errors or failed API calls, and roles without access are sent to the "no access" page.

## Things to check before going live

- Configure the official season rules.
- Load licensed data.
- Configure SMTP.
- Turn off demo data.
- Run a backup and restore test (see [OPERATIONS.md](OPERATIONS.md)).
