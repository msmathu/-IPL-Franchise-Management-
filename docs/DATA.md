# Data sources, licensing & CSV import

## Demo data

With `DemoData:Enabled=true` on an empty database, the platform creates a **fictional** league so every screen and workflow can be tried:

- 10 invented franchises (for example "Mumbai Mariners", with invented grounds, owners and colours) and about 270 invented players. Names are random combinations; any resemblance to real people is coincidental.
- Invented competitions ("Demo Premier League", "Demo Domestic T20 Trophy", "Demo T20 Internationals", …).
- **Synthetic** match-by-match statistics from a seeded generator, with season and career totals computed from them. Every row has `DataSource = "Demo generator (synthetic) — not real statistics"` and every demo entity has `IsDemo = true`. The web app shows a demo banner.
- A current season with squads, contracts, purses, an upcoming auction pool, a pending trade, scouting reports, a medical case, messages and budgets.

Demo statistics are **not real**. Don't use them for decisions.

## Real data

Load real players and statistics only from **authorised providers** under a valid licence (for example an official data feed or a licensed statistics vendor). Record the provider in the `source` column; it is shown next to every dataset. Player photographs must be HTTPS links you have the rights to use.

### Players CSV: `POST /api/v1/players/import` (or *Players → Import CSV*)

| Column | Required | Example / values |
| --- | --- | --- |
| `code` | no | Existing code updates the player; empty creates a new code |
| `full_name` | yes | |
| `display_name` | no | |
| `date_of_birth` | yes | `1998-04-21` |
| `country_code` | yes | `IND`, `AUS`, `ENG`, `RSA`, `NZL`, `WIN`, `AFG`, `SRI`, `BAN`, `IRE`, `ZIM`, `NED` |
| `batting_hand` | yes | `RightHand` / `LeftHand` |
| `bowling_style` | no | `Right-arm fast-medium` |
| `bowling_type` | no | `None, Fast, MediumFast, Medium, OffSpin, LegSpin, LeftArmOrthodox, LeftArmWristSpin` |
| `role_code` | yes | `batter, wk_batter, allrounder, bowler` |
| `secondary_role_code` | no | same values |
| `is_wicketkeeper`, `is_capped` | no | `true/false/1/0/yes/no` |
| `source` | **yes** | Provider and licence reference |

### Season statistics CSV: `POST /api/v1/statistics/import`

One row per player, competition and year. Columns: `player_code, competition_code, year, team, matches, innings, not_outs, runs, balls_faced, highest_score, highest_not_out, hundreds, fifties, fours, sixes, bowling_innings, balls_bowled, runs_conceded, wickets, best_wickets, best_runs, four_wicket_hauls, five_wicket_hauls, catches, stumpings, source`.

Rows are checked (known player and competition, valid year, innings ≤ matches, not-outs ≤ innings); bad rows are skipped and reported. Career totals are recalculated afterwards. Match-level analysis (phases, pace vs spin, venues, dismissals) needs match-level data in `ipl.Matches` and `ipl.PlayerMatchStatistics`, loaded through a provider integration.

Competitions are reference data (`ipl.Competitions`: code, name, type Franchise/Domestic/International, format T20/ListA/FirstClass/Test/ODI/T20I, level 1–5). Add your licensed competitions there before importing.
