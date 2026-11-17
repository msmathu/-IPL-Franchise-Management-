# Security, audit & compliance

## Authentication

- Passwords are hashed with ASP.NET Core Identity's `PasswordHasher` (PBKDF2-HMAC-SHA512, salted, iterated; re-hashed automatically when parameters change). Policy: 10+ characters with upper case, lower case and a digit.
- **Lockout** after 5 failed attempts for 15 minutes; login errors don't reveal whether an e-mail exists; every attempt is recorded in `LoginHistory` (IP, user agent, reason).
- **Access tokens:** JWT (HS256), 15 minutes, held only in browser memory. They carry the user's **security stamp**; the stamp is checked on every request (cached 30 s), so deactivation, role changes and password changes take effect immediately.
- **Refresh tokens:** 384-bit random values, stored as SHA-256 hashes, sent only as an `HttpOnly`, `Secure` (outside Development), `SameSite=Strict` cookie scoped to `/api/v1/auth`. They are **rotated on every use**; reusing an old token revokes the whole token family (theft detection).
- Password reset and invitation links are one-time, hashed and short-lived (1 hour / 7 days); they are sent to a queued outbox.
- Sessions can be listed and revoked by the user; administrators can deactivate users (all sessions end).

## Authorisation and data isolation

- 15 roles and 46 permissions (`backend/src/Ipl.Domain/Authorization.cs`), synced to the database on start.
- Every endpoint has a permission policy **and** the service layer re-checks the permission and the franchise scope.
- **Tenant isolation:** franchise users act for one active franchise. EF Core global query filters hide other franchises' confidential rows (contracts, finance, budgets, scouting, comparisons, Playing XIs, retention plans, medical, documents). Trade drafts and internal notes stay with the authoring franchise; other franchises' confidential records return 404.
- **Medical data:** only `medical.read`; everyone else sees a public availability status and a non-medical note. Viewing medical records is audited.
- **No privilege escalation:** franchise managers can only grant roles whose permissions they hold; only someone holding every permission can grant platform roles.
- Financial safeguards: expenses of ₹50 lakh or more need `finance.approve`; purse and trade-consideration entries can only come from the auction, retention and trade workflows; contract termination needs finance approval rights.

## Integrity

- Trades, retention submissions, auction sales and payments run in database transactions with server-side re-validation. Bids lock the lot row (`UPDLOCK`), so two franchises can't win the same increment.
- Unique constraints and idempotency keys stop double bookings; row versions detect concurrent edits (HTTP 409).
- Finance transactions and audit logs are append-only.

## Web security

- Rate limits: login, reset and invitation 10/min per IP; session refresh 60/min per IP; bids 30 per 10 s per user; API token bucket per user.
- Headers: Content-Security-Policy (no inline scripts, no third-party scripts), `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, Referrer-Policy, Permissions-Policy, HSTS (non-development).
- Input validation in every service; consistent error envelope without stack traces outside Development.
- Uploads: allow-listed types (PDF, images, Word, Excel, CSV, text), 5 MB limit, file signature check, stored in the database and downloaded only through permission checks (`Content-Disposition: attachment`).
- CSV exports neutralise spreadsheet formulas (`=`, `+`, `-`, `@`).
- Secrets: JWT key and connection strings come from user-secrets or environment variables; `.env` is git-ignored.

## Audit trail

`ipl.AuditLogs` records who, when, from which IP, the action, the entity and JSON details for:
- logins, password resets and changes, invitations, role changes, activation and deactivation
- franchise changes, player changes, imports, availability changes
- every trade step and approval decision, retention plans, submissions and releases
- auction configuration, start and stop, every bid, sale and unsold lot
- contracts, payments, expenses, budgets
- scouting reports and reviews, document uploads and downloads, medical record access and changes
- rule and setting changes

Franchise users see their franchise's log; league staff see all.

## Compliance notes

- The platform makes **no claim of official affiliation** with the IPL, BCCI or any franchise. Do not upload official logos, protected branding, player images or proprietary statistics without the appropriate rights.
- Statistics must come from **authorised data providers**; every dataset records its source and last-updated date (see [DATA.md](DATA.md)). Nothing is estimated or invented, and model outputs are labelled as estimates.
- Real-world trades, retentions and auctions must follow the official regulations and approvals of the relevant season; configure the season rules accordingly. The platform records workflows; it does not replace official league processes.
- Personal data (players' medical data, staff accounts) should be processed under your organisation's privacy policy and applicable law (for example India's DPDP Act); restrict medical access to medical staff and review access regularly.
