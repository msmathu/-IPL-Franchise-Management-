# Configuration, deployment, backup & recovery

## Configuration

Settings come from `appsettings.json`, `appsettings.{Environment}.json`, user-secrets (development) and environment variables (`Section__Key`). **Secrets never go in source control.**

| Key | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings:Default` | — | SQL Server connection string (required) |
| `Jwt:SigningKey` | — | **Required**, 32+ characters (48+ random recommended). Startup fails without it |
| `Jwt:Issuer`, `Jwt:Audience` | `ipl-franchise-platform`, `ipl-franchise-web` | |
| `Auth:AccessTokenMinutes` / `RefreshTokenDays` | 15 / 14 | |
| `Auth:MaxFailedLogins` / `LockoutMinutes` | 5 / 15 | |
| `Auth:AppBaseUrl` | `http://localhost:4200` | Used in e-mailed links (reset, invitation) |
| `Database:InitializeOnStartup` / `Migrate` | true / true | Applies migrations, syncs roles/permissions, enables read-committed snapshot |
| `DemoData:Enabled` | false (true in Development) | Creates the fictional demo league on an **empty** database |
| `DemoData:HistoricSeasons` | 5 | Past seasons to simulate (1–5) |
| `Redis:Connection` | empty | Empty = in-memory cache |
| `Email:SmtpHost`, `SmtpPort`, `Username`, `Password`, `From`, `EnableSsl` | empty | Empty host = e-mails stay in `ipl.EmailMessages` |
| `Cors:Origins` | `http://localhost:4200` | Only needed when the web app is on another origin |
| `RateLimits:AuthPerMinute` / `RefreshPerMinute` / `ApiBurst` / `ApiPerSecond` | 10 / 60 / 300 / 20 | Per IP (auth) or per user (API) |
| `ReverseProxy:Enabled` | false | Turn on behind a proxy/load balancer so client IPs are logged correctly |
| `Swagger:Enabled` | false (on in Development) | |
| `Serilog:*` | | Structured JSON logs to stdout |

**Season rules** (purse, squad limits, retention costs, bid increments, trade window, approvals) live in the database and are edited in *Settings → Seasons & rules*. The demo values are placeholders: set the official values for each season from the league's regulations.

## Running in production

1. Provision SQL Server (2019+ or Azure SQL) and, optionally, Redis.
2. Build the image: `docker build -t franchise-hq .` (or `dotnet publish` plus `ng build`, copying `frontend/dist/web/browser` to the API's `wwwroot`).
3. Set at least `ConnectionStrings__Default`, `Jwt__SigningKey`, `Auth__AppBaseUrl`, `ASPNETCORE_ENVIRONMENT=Production`, and SMTP settings.
4. Put it behind HTTPS (reverse proxy or platform TLS) and set `ReverseProxy__Enabled=true`. HSTS is sent outside Development.
5. On first start the database is migrated and the 15 roles and 46 permissions are created. Create the first super admin with the demo seeder (`DemoData__Enabled=true` on an empty database, then deactivate the demo users), or insert a user and assign `super_admin` in SQL.
6. Single instance: SignalR and the memory cache work out of the box. **Several instances** need Redis for the cache, a SignalR backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) and sticky sessions or WebSockets only. Run only one instance with `Database:InitializeOnStartup=true`.

Health check: `GET /health` checks the database.

## Backup & recovery

- **Full backup** daily and **log backups** every 15 minutes (`RECOVERY FULL`) give a recovery point of about 15 minutes. Keep backups off the database server; encrypt them (`WITH ENCRYPTION`).
  ```sql
  BACKUP DATABASE IplFranchise TO DISK = N'X:\backup\IplFranchise_full.bak' WITH COMPRESSION, CHECKSUM;
  BACKUP LOG IplFranchise TO DISK = N'X:\backup\IplFranchise_log.trn' WITH COMPRESSION, CHECKSUM;
  ```
- **Restore test** monthly into a separate database; run the app against it and check the `/health` endpoint and a few screens.
- **Point-in-time restore:** restore the last full backup `WITH NORECOVERY`, then the logs `WITH STOPAT = '<time>'`.
- **What must be backed up:** only the database. Uploaded documents and data-protection keys are stored in it; the containers are stateless.
- **After a restore:** users may need to sign in again (refresh tokens before the restore point are still valid; revoke them with `UPDATE ipl.RefreshTokens SET RevokedAt = SYSUTCDATETIME()` if needed).
- **Audit logs and finance transactions** are append-only by design; do not delete them during clean-ups.

## Upgrading

`dotnet ef migrations add <Name> -p backend/src/Ipl.Infrastructure -s backend/src/Ipl.Api -o Persistence/Migrations` creates a migration; it is applied automatically on start (or generate a script with `dotnet ef migrations script --idempotent` for DBA-controlled releases and set `Database:Migrate=false`).
