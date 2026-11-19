# Data sources, licensing & CSV import

## Reference data seeded on first start

With `DemoData:Enabled=true` on an empty database:

- **The 10 real franchises** (Chennai Super Kings, Mumbai Indians, Royal Challengers Bengaluru, Kolkata Knight Riders, Delhi Capitals, Rajasthan Royals, Punjab Kings, Sunrisers Hyderabad, Lucknow Super Giants, Gujarat Titans): names, codes, cities, home grounds, owners as publicly reported, team colours and IPL finals 2008–2025 (champion and runner-up only).
- **243 real players in their IPL 2026 squads**, from the published post-auction lists (Outlook India, cross-checked with Wisden; see `backend/src/Ipl.Infrastructure/Persistence/RealSquads.cs`). Recorded: name, team, role group, country, overseas/capped status, keeper, and batting hand / bowling style where publicly known. **Not recorded** (shown as "—" / "Not recorded"): dates of birth, prices (except the few stated by the sources), injuries. Nothing about real players is invented. Statistics come from Cricsheet (below).
- The current season (2027) starts with these squads carried forward, because official 2027 retention lists were not published at the time of seeding. Update squads through the retention, trade and auction workflows (or import).
- **Fictional "demo prospects"**: 48 invented players, clearly named "… (Demo)" and flagged `IsDemo`, with **synthetic** statistics (`DataSource` says so), used for the practice auction, scouting and the analytics screens.
- Demo user accounts (fictional people) for every role; no demo trades, injuries or medical records are created for real players.

### Images and logos

**Player photos** come from **Wikimedia Commons**, where every file is freely licensed (Creative Commons or public domain). *Players → Get free photos* (`players.manage`, or `POST /api/v1/players/photos/import`) identifies each player exactly (Cricsheet id → ESPNcricinfo id in the Cricsheet register → Wikidata item with that id → its image, or the free lead image of the player's English Wikipedia article), downloads a 320 px version into `Media:Folder` (default `App_Data/media`, served at `/media`) and stores the **author, licence and source page**, shown under the photo on the profile, as the licences require. About a third of the squad has a free photo; the rest show initials. Photos set by hand are never replaced. Back up the media folder with the database.

**Team logos are not downloaded**: IPL franchise logos are trademarks and are not freely licensed (Wikipedia only uses them under "fair use"). Franchises show their short code on the team colour. To show a logo you have permission to use, save its https URL in *Franchise → Edit → Logo URL*. Player photos can also be set by hand (https URL you have the rights to use) in *Player → Career & availability → Photo*; broken links fall back to the badge.

## Official squad announcements (retentions, releases, trades)

Squads do not change by themselves: real signings, trades and releases are recorded in the app. For the league's official announcement (for example the retention-day media advisory), a league administrator posts it to `POST /api/v1/league/official-squads` (`seasons.manage`): the confirmed trades, each team's retained players and remaining purse, and the season purse. In one transaction the app moves traded players (squad rows marked *Trade* with the fee, team history), releases everyone not retained (release records, back to the auction pool), stores a confirmed retention list per team and sets each purse through the ledger. Everything is audited.

The current season uses the **official IPL 2027 retention list** (IPL media advisory, iplt20.com, 18 Nov 2026): 184 retained players (55 overseas), 89 released, 14 confirmed trades (fees as reported by CricTracker), purse ₹130 Cr per team with the official remaining purses. Six players missing from the earlier 2026 data were added (Pat Cummins, Josh Hazlewood, Josh Inglis, Nathan Ellis, Harshit Rana, Yash Dayal) with public profile facts only. A database backup taken before the change is in SQL Server's default backup folder (`IplFranchise_before_ipl2027_squads_*.bak`).

## Statistics from Cricsheet (ball-by-ball)

[Cricsheet](https://cricsheet.org) publishes ball-by-ball records of every IPL match since 2008 and of international matches under the **Open Data Commons Attribution License (ODC-BY 1.0)**: free to use, including commercially, as long as Cricsheet is credited. Every statistic loaded from it carries the source "Cricsheet (cricsheet.org), ODC-BY 1.0", which the statistics screens show.

**Running it:** *Player Statistics → Import / update from Cricsheet* (`stats.import`), or `POST /api/v1/statistics/cricsheet/import`; status at `GET /api/v1/statistics/cricsheet`. It also runs automatically every `Cricsheet:AutoImportDays` days. A run downloads `ipl_json.zip` and `t20s_male_json.zip` (about 20 MB; cached and only downloaded again when Cricsheet publishes a newer file), then:

1. **Links squad players to Cricsheet people.** Cricsheet uses scorecard names ("V Kohli", "SA Yadav"), so a link needs the name to fit (surname plus initials, small spelling differences) **and** the person to have played for the player's current franchise in the last three IPL seasons (overseas players: for their country in T20Is). Exact full-name matches of recent players are also accepted, plus a short hand-checked list (`RealSquads.CricsheetIds`). Anything ambiguous is left unlinked. A link can be set or removed by hand on the player profile (*Career & availability → Cricsheet link*).
2. **Loads every match with a linked player** into `ipl.Matches` / `ipl.PlayerMatchStatistics` (runs, balls, boundaries, dismissals, phases, wickets, economy, dots, maidens, catches, stumpings, run-outs). Re-runs only add what is new.
3. **Recalculates** season and career totals, so ratings, valuations, comparisons, form and team strength use the real numbers.
4. **Refreshes player photos** from Wikimedia Commons (`Cricsheet:RefreshPhotosAfterImport`, on by default), so newly linked players get a photo without anyone pressing the button.
5. **Rebuilds scorecards, season totals for everyone who played and batter-v-bowler records** (IPL), used by the scorecard, leaders, records, head-to-head and match-up screens.

Known limits:
- Players who have not played an IPL match or a T20I yet have no data (about 20 uncapped squad players); they're listed on the Player Statistics page.
- Cricsheet's T20I archive is not complete (for example it currently has no Afghanistan T20Is, and a few India matches are missing), so T20I totals can be lower than official records. IPL coverage is complete.
- Pace/spin splits and wickets against right/left-handers need the bowler's type or batter's hand, which Cricsheet does not record; they are counted only where the other player is in our database with that detail.
- Home/away is worked out for IPL matches only (from the franchise's home cities).
- Prices, dates of birth and photos are not in Cricsheet.

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
