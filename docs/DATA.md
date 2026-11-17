# Data sources, licensing & CSV import

## Reference data seeded on first start

With `DemoData:Enabled=true` on an empty database:

- **The 10 real franchises** (Chennai Super Kings, Mumbai Indians, Royal Challengers Bengaluru, Kolkata Knight Riders, Delhi Capitals, Rajasthan Royals, Punjab Kings, Sunrisers Hyderabad, Lucknow Super Giants, Gujarat Titans): names, codes, cities, home grounds, owners as publicly reported, team colours and IPL finals 2008–2025 (champion and runner-up only).
- **243 real players in their IPL 2026 squads**, from the published post-auction lists (Outlook India, cross-checked with Wisden; see `backend/src/Ipl.Infrastructure/Persistence/RealSquads.cs`). Recorded: name, team, role group, country, overseas/capped status, keeper, and batting hand / bowling style where publicly known. **Not recorded** (shown as "—" / "Not recorded"): dates of birth, prices (except the few stated by the sources), statistics, injuries. Nothing about real players is invented.
- The current season (2027) starts with these squads carried forward, because official 2027 retention lists were not published at the time of seeding. Update squads through the retention, trade and auction workflows (or import).
- **Fictional "demo prospects"**: 48 invented players, clearly named "… (Demo)" and flagged `IsDemo`, with **synthetic** statistics (`DataSource` says so), used for the practice auction, scouting and the analytics screens.
- Demo user accounts (fictional people) for every role; no demo trades, injuries or medical records are created for real players.

### Images and logos

No logos or photos are bundled: official league/franchise logos are trademarks and player photos are copyrighted. The app shows the franchise's short code on its team colour, and player initials. To show real images, save an **https URL to an image you have the rights to use** (your own photographer, an agency licence, the franchise's permission, or a Wikimedia Commons file used under its licence): player photos in *Player → Career & availability → Photo* (`players.manage`), franchise logos in *Franchise → Edit → Logo URL*. Broken links fall back to the badge.

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
