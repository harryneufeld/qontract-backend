# Authenticator-based two-factor authentication

Accounts opt in from Account → Two-factor authentication in the matching Flutter
app. Existing accounts remain password-only. This is RFC 6238 TOTP with a unique
160-bit key, SHA-1, six digits and 30-second steps; one adjacent step in either
direction accommodates clock drift. Run the API host with synchronized time.

## API

`POST /api/auth/login` keeps its existing email/password fields. After valid
credentials for an enrolled account it returns `requiresTwoFactor: true`, an
empty access token and no user. **No session is created.** Resubmit email/password
with `twoFactorCode` and optional `useRecoveryCode: true`. Only a complete proof
returns the usual auth response. No password or incomplete proof is stored in
the app's local storage, and no temporary bearer token grants account access.
The password stays in the login form's memory until sign-in/cancellation.

Authenticated account endpoints (both `/api/auth` and `/api/v1/auth`):

- `GET two-factor`: enabled status and remaining recovery-code count.
- `POST two-factor/setup`: `{password}` returns `secret` and `authenticatorUri`.
  A new attempt replaces only the pending key, valid for ten minutes.
- `POST two-factor/enable`: `{password, code}` confirms the pending key and
  returns ten `recoveryCodes`, shown once. It revokes all existing sessions.
- `POST two-factor/disable`: `{password, code, useRecoveryCode}` checks both
  factors, removes the enrollment and revokes all existing sessions.

After enrollment, save the codes before acknowledging the screen and signing in
again. The confirmation code is already consumed; use the next authenticator
code or one recovery code. Setup supports copying the key for manual entry and
opening an installed app through the `otpauth` URI. It does not use an external
QR-code service or send keys to one. To obtain a fresh recovery-code set, disable
2FA using a remaining code and enroll again.

TOTP steps and recovery codes use an atomic compare-and-swap to prevent reuse,
including concurrent requests. Only hashes of recovery codes are stored. Five
failed factor checks lock the account's factor checks for five minutes, in
addition to the existing IP rate limits. Password changes and email password
resets preserve enrollment. If the authenticator and every recovery code are
lost, there is no automatic email bypass or administrator reset endpoint.

## Deployment and backup

Deploy the backend migration `AddTwoFactorAuthentication` before the app. The
migration adds nullable key columns and disabled defaults; it does not enroll
accounts or change their passwords. SQLite migration execution and SQL generation
for PostgreSQL/SQL Server are covered by regression tests; validate upgrades on
the chosen provider before rollout.

Authenticator keys are encrypted using ASP.NET Core Data Protection with the
application name `qontract` and purpose `qontract.totp.v1`. Outside Development,
the default key-ring directory is `<content-root>/data/keys` (`/app/data/keys`
in the supplied Docker image). All supplied compose stacks already retain
`/app/data` in their database volume. An external database still requires this
local volume. Override with `DataProtection__KeyPath` for a different persistent
directory; all replicas must share the same key ring and application name.
Development uses the platform's normal Data Protection key location.

Restrict access to the key directory, encrypt host volumes/backups as appropriate,
and back up **both the database and the complete key ring**. On Linux, persisted
key XML is not automatically encrypted at rest. Do not commit or expose it.
Losing the key ring prevents authenticator verification; a separately saved
recovery code can still sign in and disable/re-enroll 2FA. Restore both together
and verify a synthetic enrolled account after container recreation. Keep the key
ring across application upgrades and do not delete retired keys while stored
enrollments still depend on them.

After rollback to an older API, enrolled accounts would no longer receive a
second-factor check. Do not run an older backend against enrolled accounts;
coordinate rollback and do not silently downgrade authentication.
