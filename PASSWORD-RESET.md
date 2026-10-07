# Email password reset and migration onboarding

Implemented endpoints and UI are reviewed through backend #54 and app #46.
Dedicated test-mailbox delivery and password setup were verified on ARM64 LAN
staging on 2026-10-08. Production onboarding remains pending.

## Deployment configuration

Apply `AddPasswordReset` after the session migration and deploy the matching app
and backend. The feature defaults to disabled; both public endpoints return 503
when disabled. Enabling it requires valid settings at startup:

| Environment variable | Example / requirement |
| --- | --- |
| `PasswordReset__Enabled` | `true` after staging validation |
| `PasswordReset__PublicAppUrl` | `https://qontract.example/app/` — trusted public Flutter URL, including its hosting subpath |
| `Smtp__Host` | Hostname of the existing SMTP service |
| `Smtp__Port` | `587` for required STARTTLS, or the server's implicit-TLS port |
| `Smtp__TlsMode` | `StartTls` (default) or `SslOnConnect`; plaintext/opportunistic TLS is rejected |
| `Smtp__From` | A valid sender mailbox accepted by the SMTP service |
| `Smtp__Username` / `Smtp__Password` | Inject both through host secrets, or neither for an explicitly configured trusted TLS relay |

The public URL must use HTTPS and contain no credentials, query or fragment.
The API never trusts request Host/redirect parameters for email links. Certificate
validation remains enabled. SMTP has a 20-second network timeout and delivery work
has a 30-second overall bound. No SMTP protocol/payload logging is configured.
Do not paste credentials or reset links in chat/issues. Configure DNS/sender
authorization and reverse-proxy request-body redaction before use.

## Behaviour

- `POST /api/auth/forgot-password` with `{ "email": "user@example.com" }`
  returns the same 202 acknowledgement for eligible, inactive and unknown accounts.
  It queues the address without checking the account or waiting for SMTP. Input
  validation, auth-IP rate limits and generic disabled/full-queue 503 responses
  apply independently of account existence.
- A bounded in-memory worker queue holds at most 128 addresses, not raw tokens.
  Queue acceptance is not proof of delivery. Pending requests can be lost during
  restart; users may request again. There is no automatic retry or durable email
  outbox. Failed delivery consumes the generated token, and logs only failure type,
  without recipient, provider message, credentials, link or body.
- Eligible active accounts receive at most one email per minute and three per hour,
  enforced against database request records in a serializable transaction. Failed
  attempts count toward these limits. A new issued link supersedes previous links;
  requests suppressed by throttling leave the current link unchanged.
- A 32-byte random token is emailed; only its SHA-256 hash is stored. Links expire
  after 30 minutes, are bound to account/email/authentication version and are
  consumed once. Do not enable sensitive-data SQL logging or request-body capture
  for these endpoints. Neither reset passwords nor raw reset tokens are persisted
  in a delivery queue/log.
- `POST /api/auth/reset-password` with `{ "token": "...", "newPassword": "..." }`
  accepts the same 6–255 character password policy as existing password changes.
  One transaction consumes the token, installs a PBKDF2 hash, clears password-setup
  state and increments `AuthVersion`, revoking all earlier sessions and reset links.
  Invalid/expired/reused links, changed account state and disabled users cannot
  reset credentials. Concurrent redemptions cannot both succeed. Normal email
  changes through user administration increment the version too, so changing an
  address back does not revive old links. Password changes invalidate links by version.
- Completion returns a message, never a login token. The app clears stored sessions
  and asks the user to sign in. Reset is not a factor-disable or recovery bypass;
  future TOTP configuration must remain enabled after password reset.

## Two-user FamilyPlanner migration

Do not copy legacy password hashes or create duplicate accounts. The importer must
preserve source-ID mappings, memberships and document ownership, validate normalized
email uniqueness, and record operator confirmation of each destination email.
Create migrated accounts with `RequiresPasswordSetup = true` and an undisclosed
random password hash; keep account activity independent of this setup state.
Login and authenticated token validation reject setup-required accounts. Reset
delivery allows active setup-required accounts and clears this flag after proof.
Do not bulk-mark existing refactor users: the migration defaults the flag to false
and preserves their hashes, authentication versions and audit fields.

After the production cutover gates are satisfied, request links through the public flow for the
two accounts under the agreed cutover/onboarding plan. No source account/session
is modified. Accounts without an active tenant membership sign in to their personal
workspace; tenant memberships and access must still be reconciled during migration.

## Verified staging email rehearsal (2026-10-08)

The native ARM64 Docker deployment behind Traefik was tested with a dedicated
SMTP sender and recipient mailbox. SMTP authentication used required STARTTLS on
port 587; recipient verification used IMAP over TLS on port 993. Both connections
validated their server certificates. Only the LAN HTTPS proxy used its staging
self-signed certificate; mail certificate validation remained enabled.

An isolated active test account started with `RequiresPasswordSetup = true`, the
ordinary user role and no tenant memberships. The public flow verified:

- Login was rejected before password setup.
- A reset request returned the generic 202 response and the email arrived in INBOX.
- The link targeted the configured HTTPS app using the hash-router reset route.
- Redemption succeeded without returning a login token; reuse returned 400.
- The new password allowed sign-in to the personal workspace, and its contract
  list was empty.

Credentials were supplied through the host's protected API environment file.
The test retained only outcome metadata; mailbox passwords, the generated app
password and reset link were excluded from that evidence. No production accounts
or legacy data were used. This proves delivery for the tested provider and inbox,
not spam placement across other providers, production DNS or production onboarding.
Browser URL scrubbing, mobile layout, subpath hosting, expiry and failure/restart
checks remain part of the deployment gate below.

## App links and verification gate

Links use the Flutter default hash router:
`https://qontract.example/app/#/reset-password?token=...`. The token stays in the
fragment, avoiding server query/referrer transmission. On opening, the app captures
it in memory, replaces the visible fragment with `#/reset-password`, and stores a
route name without the token. Passwords/tokens are not placed in preferences or
analytics. Reloading after URL scrubbing deliberately shows an incomplete-link
screen; reopen the original email or request a fresh link. Coordinate base href,
HTTPS API URL and CORS with the Docker deployment. Native app/universal-link OS
associations are not provisioned here; browser opening is the initial delivery path.

Before enabling for real users, verify: trusted sender/TLS authentication, actual
mailbox and spam delivery, hosting-subpath links, startup and mobile browser layout,
URL scrubbing/referrer policy, expiry/reuse, old-session rejection, restart/failure
behaviour, and no secrets in proxy/application/crash logs. SQLite runtime tests and
PostgreSQL/SQL Server migration-script checks are available; runtime rehearsal on
the selected target provider is still required. Define retention/cleanup for expired
reset records with the operator. A real email was delivered only to the dedicated
staging test mailbox; no production deployment or onboarding has occurred.

Restoring an older database can otherwise revive consumed links or revoked
sessions. Before accepting traffic after restore, invalidate all restored reset
records and sessions and advance account authentication versions under the
reviewed recovery runbook; coordinate JWT signing-key rotation where required.
Rehearse this on a copy, including the protection of any future enrolled factors.
