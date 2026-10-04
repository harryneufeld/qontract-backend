# Qontract Backend - Multi-tenant Contract Management System

The .NET 10 API for the qontract refactor. FamilyPlanner is the production
system and the reference for full feature parity and complete migration.
This backend is not yet a complete replacement for it.

## Features

- ✅ Multi-tenant data model (Family & Company tenants)
- ✅ Personal workspaces with owner-only contracts, contractors and documents
- ✅ Clean Architecture with Domain/Application/Infrastructure separation
- ✅ Entity Framework Core with migration support
- ✅ File upload/download for contract documents
- ✅ CRUD operations for all entities
- ✅ JWT-based authentication and authorization
- ✅ Soft delete for audit trails
- ✅ Versioning for document uploads

## Current limitations and agreed requirements

- Tenant/user CRUD and role authorization exist. Tenant administration now uses
  active admin memberships; creating a tenant atomically gives its creator an
  admin membership. Migration `AddTenantMemberships` backfills existing users.
  Login, user administration, and authorization use membership-specific roles.
  Multiple tenant memberships were approved by Harry in
  [#13](https://github.com/harryneufeld/qontract-backend/issues/13#issuecomment-5804173333).
  Membership management, switching, code invitations, and registration are implemented.
- Public registration is controlled by `Registration:AllowPublic`, default false;
  set `Registration__AllowPublic=true` in deployment configuration to enable it.
  Valid invitations permit signup independently of public registration. A deployment-wide
  frontend control for a platform administrator is proposed separately from
  tenant administration. Invitation codes work for existing users and new accounts;
  invitation links and validated tenant settings remain follow-ups.
- Tenant administrators can list/read/update/deactivate only tenants where they
  have an active admin membership, with an active account. Foreign and unknown
  IDs return the same 404; the JWT role alone does not grant tenant access.
- Authentication issues 24-hour JWTs linked to persisted, revocable sessions.
  Logout revokes the current session, including tokens issued during tenant
  switches; independent logins stay active. A successful password change atomically
  increments the account authentication version, invalidating all earlier sessions.
  The app then requires a fresh login. Refresh tokens and session-management UI
  remain follow-ups in [#44](https://github.com/harryneufeld/qontract-backend/issues/44).
  Every authenticated request checks account and session activity; tenant requests
  also check membership, tenant activity and role. Removing a membership immediately rejects its tokens; demotion updates
  their effective role. Membership removal preserves the account and other memberships,
  and the final active tenant administrator cannot be removed or demoted.
- Contract amounts support all six legacy billing periods and dated price revisions.
  Explicit billed values are decimal-validated invariant strings, preserving API compatibility.
  Existing unclassified values remain unchanged with an unknown period. Contract updates
  accept `PriceEffectiveFrom` and `PriceNotes`; details/lists return `PriceHistory`.
  See [LEGACY-MAPPING.md](LEGACY-MAPPING.md) for date boundaries, estimates, remaining
  legacy gaps and import assumptions, tracked in [#47](https://github.com/harryneufeld/qontract-backend/issues/47).
- Contracts support verbatim IBAN, multiline contract notes and the five legacy
  business types, independent of workflow status and billing period. Existing
  classifications remain unknown. Personal ownership is implemented; historical
  import and source ownership reconciliation remain pending in [#51](https://github.com/harryneufeld/qontract-backend/issues/51);
  see [LEGACY-MAPPING.md](LEGACY-MAPPING.md).
- Contractors support verbatim website, description and multiline notes through
  CRUD and activation changes. Omitted/null updates preserve text and empty strings
  clear it. Personal/family company ownership and historical import still require
  reconciliation in [#52](https://github.com/harryneufeld/qontract-backend/issues/52);
  see [LEGACY-MAPPING.md](LEGACY-MAPPING.md).
- Invoice records and shared contract-document links are implemented. Invoice amounts
  are exact signed decimal strings with optional currency; evidence references a specific
  file version. Active evidence is protected from deletion, and archived invoice references
  prevent physical file purge. Historical ownership/provenance and staged import
  remain gaps. See [LEGACY-MAPPING.md](LEGACY-MAPPING.md) for the implemented boundary.
  Remaining invoice/file parity is tracked in [#50](https://github.com/harryneufeld/qontract-backend/issues/50).
- Legacy families map to refactor tenants. Tenant-shared and personal account file
  libraries support uploads without a contract, metadata/categories, versions and
  trash/restore. New owners come from authenticated identity; replacement versions
  retain the original owner. Personal files follow their account across tenant switches
  and remain private even from tenant admins. Historical ownership/provenance remains
  tracked in [#48](https://github.com/harryneufeld/qontract-backend/issues/48).
- Personal contracts and contractors are owner-only, with immutable scope and
  same-workspace contractor references. Account → Workspaces selects Personal or
  a tenant; accounts without memberships can log in to Personal. Private contract
  documents and addresses enforce the same boundary. Tenant admins cannot bypass
  it. Migration adds explicit ownership without inferring or changing historical
  rows. See [LEGACY-MAPPING.md](LEGACY-MAPPING.md) for the API and rollback requirements.
- Current migration scope preserves memberships, current prices and periods,
  personal/tenant documents, shared-file relationships, addresses and audit data.
  Credentials use email password setup. Unused production `Invoices`, `PriceHistories`
  and `ContractPriceHistory` are excluded by agreement. See the proposed
  [export plan](https://github.com/harryneufeld/FamilyPlanner/issues/2).
- Audit cleanup is approved in [#38](https://github.com/harryneufeld/qontract-backend/issues/38#issuecomment-5784379193):
  remove contract Creator/Modifier relationships and the unused new-backend join;
  keep `Documents.UserId`; consolidate upload audit fields into base audit fields.
  These schema changes are not yet implemented. Verify actual data before dropping
  columns/tables; preserve legitimate legacy shared-file associations in migration.
  The requested ownership investigation is tracked in [#48](https://github.com/harryneufeld/qontract-backend/issues/48).

Agreed order: documentation cleanup; tenant access/membership; app usability
([app #38](https://github.com/harryneufeld/qontract-app/issues/38)); parity/migration
planning; revocable sessions. Staging and production target the owner's Linux
Docker machine. GitHub Pages will not be used.

## Architecture

```
Qontract/
├── Qontract.Domain/           # Entities, value objects, domain logic
│   └── Models/
│       ├── Tenant.cs
│       ├── User.cs
│       ├── Contractor.cs
│       ├── Contract.cs
│       ├── Document.cs
│       └── Address.cs
├── Qontract.Application/      # CQRS, DTOs, use cases
│   ├── Models/                # DTOs
│   ├── Services/              # Business logic services
│   └── [Interfaces]
├── Qontract.Core/             # Shared file-storage interface and security constants
├── Qontract.Infrastructure/   # EF Core, file storage, auth
│   ├── Data/                  # DbContext, migrations
│   └── FileStorage/           # File handling
└── Qontract.API/              # Controllers, dependencies
    └── Controllers/
```

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- No external database required — SQLite is embedded and created automatically on first run

### Installation

1. Restore dependencies:
```bash
dotnet restore
```

2. (Optional) The database connection is `Data Source=QontractDb.sqlite` in `appsettings.json`. Migrations run automatically at startup.

   > **JWT signing key (issue #36):** `appsettings.json` intentionally does
   > **not** contain `Jwt:Key`. In Development the API falls back to a shared
   > development-only key (`Qontract.Core.Security.JwtKeys`) so it runs out of
   > the box. In every non-Development environment the API fails fast at
   > startup unless the key is provided via the `Jwt__Key` environment
   > variable (or user secrets), e.g.
   > `Jwt__Key="$(openssl rand -base64 48)" dotnet run --project Qontract.API`.
   > Docker Compose reads `Jwt__Key` from the protected deployment env file;
   > see [DEPLOY.md](DEPLOY.md).

   > **File storage (issue #25):** document files are stored behind
   > `IFileStorage`, selected by `Storage:Provider` in `appsettings.json`:
   > `local` (default — disk under `Storage:Path`), `azure` (Azure Blob
   > Storage, settings in `Storage:Azure`) or `s3` (Amazon S3, settings in
   > `Storage:S3`). The provider-specific credentials are intentionally
   > **not** committed: supply them via environment variables
   > (`Storage__Azure__ConnectionString`; for S3, `Storage__S3__Bucket` plus
   > the standard AWS credential chain) or user secrets. The maximum file
   > size (`Storage:MaxFileSizeBytes`) applies to all backends.

3. Build and run:
```bash
dotnet run --project Qontract.API
```

4. API is available at:
   - HTTP: `http://localhost:65533`
   - HTTPS: `https://localhost:65532`
   - Swagger UI: `https://localhost:65532/swagger` (or `http://localhost:65533/swagger`)

5. In Development only, demo tenants and admin users are created on first run:
   - Email: `admin@qontract.com`
   - Password: `password123`

> Note: the Flutter app expects the API at `http://localhost:65533/api` and
> the web client on `http://localhost:3000` (CORS).

## API Endpoints

### Authentication
- `POST /api/Auth/login` - Login and get JWT token
- `GET /api/Auth/me` - Validate current user/session
- `POST /api/Auth/logout` - Revoke the authenticated session
- `POST /api/Auth/change-password` - Change current user's password
- `GET /api/Auth/registration` - Public signup policy
- `POST /api/Auth/register` - Public or invitation-based signup
- `GET /api/Auth/memberships` - Active memberships for the account
- `POST /api/Auth/switch-tenant` - Issue a token for an active membership
- `POST /api/Auth/invitations/redeem` - Join by code and issue the selected tenant's token

### Tenants
- `GET /api/Tenants` - List tenants administered by the caller
- `GET /api/Tenants/{id}` - Get tenant details
- `POST /api/Tenants` - Create new tenant
- `PUT /api/Tenants/{id}` - Update tenant
- `PATCH /api/Tenants/{id}/deactivate` - Deactivate tenant
- `GET /api/Tenants/{id}/members` - List members (tenant admin)
- `PATCH /api/Tenants/{id}/members/{userId}` - Change membership role/activity
- `DELETE /api/Tenants/{id}/members/{userId}` - Deactivate membership, preserving the account
- `GET /api/Tenants/{id}/invitations` - List invitations without secrets
- `POST /api/Tenants/{id}/invitations` - Create an invitation; returns the code once
- `DELETE /api/Tenants/{id}/invitations/{invitationId}` - Revoke an unused invitation

### Users
- `GET /api/Users` - List users in tenant
- `GET /api/Users/{id}` - Get user details
- `POST /api/Users` - Create new user
- `PUT /api/Users/{id}` - Update user
- `PATCH /api/Users/{id}/deactivate` - Deactivate user

### Contractors
- `GET /api/Contractors` - List contractors in tenant
- `GET /api/Contractors/{id}` - Get contractor details
- `POST /api/Contractors` - Create new contractor
- `PUT /api/Contractors/{id}` - Update contractor
- `PATCH /api/Contractors/{id}/deactivate` - Deactivate contractor
- `PATCH /api/Contractors/{id}/activate` - Reactivate contractor
- `GET /api/Contractors/{id}/addresses` - List active contractor addresses

### Contracts

For a dated price revision, `PUT /api/Contracts/{id}` accepts, for example:

```json
{"value":"120.50","currency":"EUR","billingPeriod":"Monthly","priceEffectiveFrom":"2026-11-01","priceNotes":"New tariff"}
```

Providing an effective date or note records a revision even when the amount is
unchanged. Omitting all price fields during an unrelated edit leaves history alone.
Explicit prices use plain decimal strings (a decimal comma is accepted and normalized);
omit the period to preserve an unclassified legacy value until it is reviewed.

- `GET /api/Contracts` - List all contracts
- `GET /api/Contracts/stats` - Tenant-wide dashboard counters
- `GET /api/Contracts/{id}` - Get single contract
- `POST /api/Contracts` - Create new contract
- `PUT /api/Contracts/{id}` - Update contract
- `PATCH /api/Contracts/{id}/deactivate` - Deactivate contract
- `DELETE /api/Contracts/{id}` - Soft delete contract

### Documents
- `GET /api/Documents?scope=Tenant&deleted=false&page=1&pageSize=50` - Current tenant library (page size 1–200)
- `GET /api/Documents?scope=Personal` - Signed-in account's private library, independent of active tenant
- `POST /api/Documents?scope=Tenant` or `scope=Personal` - Upload without a contract; server sets tenant/owner
- `GET /api/Documents/contract/{contractId}/documents` - List documents for a contract
- `POST /api/Documents/{id}/contracts/{contractId}` - Share the current file with another same-tenant contract (idempotent)
- `DELETE /api/Documents/{id}/contracts/{contractId}` - Remove a secondary contract link; preserve bytes and other links
- `POST /api/Documents/contract/{contractId}/documents` - Upload document (JSON/base64)
- `GET /api/Documents/{id}` - Get document metadata
- `GET /api/Documents/{id}/download` - Download document
- `PUT /api/Documents/{id}` - Update document
- `DELETE /api/Documents/{id}` - Delete document
- `GET /api/Documents/trash` - List documents within soft-delete retention
- `POST /api/Documents/{id}/restore` - Restore deleted document
- `POST /api/Documents/{id}/reupload` - Upload a replacement version

Upload/metadata payloads also accept optional `description` (up to 2,000 characters)
and `category` (up to 100), alongside title/date. Blank metadata clears its value;
null metadata leaves it unchanged. `UserId` and `TenantId` are server-owned. Scope
cannot be changed through metadata. Personal files are rejected as shared contract
links or tenant invoice evidence. Deleted library entries include `deletedAt` and
`expiresAt`, or `retainedForInvoice=true` with no purge deadline.

The library schema preserves existing paths, owner IDs and audits and backfills
tenancy from existing primary contracts only. Unknown owners stay null. Restore a
verified backup for rollback: this migration refuses a destructive downgrade to
mandatory contract ownership.

### Invoices

- `GET /api/Invoices?contractId=...&deleted=false&page=1&pageSize=50` - Paged current-tenant list; contract filter optional, page size 1–200
- `GET /api/Invoices/{id}` - Read a tenant-owned invoice, including archived records
- `POST /api/Invoices` - Create an invoice
- `PUT /api/Invoices/{id}` - Replace all editable fields on an active invoice
- `DELETE /api/Invoices/{id}` - Archive an invoice; keep its evidence
- `POST /api/Invoices/{id}/restore` - Restore an invoice (restore its evidence first if trashed)

Create/update fields: `invoiceDate` (ISO date), optional `dueDate`, `amount`
(exact signed decimal string), optional three-letter `currency`, `invoiceNumber`,
`description`, `contractId`, and `documentId` (exact version). Omitted currency is
unknown. Referenced contracts and documents must belong to the current tenant;
when both are supplied the document must be associated with that contract.
Lists return arrays; request subsequent pages until a short page is returned.

### Addresses
- `POST /api/Addresses` - Create address
- `PUT /api/Addresses/{id}` - Update address
- `GET /api/Addresses/{id}` - Get address
- `PATCH /api/Addresses/{id}/deactivate` - Deactivate address

## Development

### Running with Docker

Follow [DEPLOY.md](DEPLOY.md) to choose a provider, supply protected secrets,
configure persistent storage and start the API behind the HTTPS proxy. The Compose
file uses Production and does not create demo accounts.

### Testing

```bash
dotnet test
```

### Swagger UI

Open `https://localhost:65532/swagger` (or `http://localhost:65533/swagger`) to view interactive API documentation.

## Database Provider (issue #26)

The backend defaults to **SQLite** (zero-setup, file `QontractDb.sqlite` next to the API). For
production deployments it can run on **PostgreSQL** or **SQL Server**:

| `Database:Provider` | Aliases                    | `ConnectionStrings:DefaultConnection`                |
|---------------------|----------------------------|------------------------------------------------------|
| `sqlite` (default)  | —                          | `Data Source=QontractDb.sqlite`                     |
| `postgresql`        | `postgres`, `pg`, `npgsql` | `Host=...;Port=5432;Database=qontract;Username=...;Password=...` |
| `sqlserver`         | `mssql`, `sql-server`      | `Server=...;Database=qontract;User Id=...;Password=...`      |

Set the value in `appsettings.json` or via the environment variable `Database__Provider`.
The same migration chain runs on all three providers (the migrations only use DDL that is
valid on SQLite, PostgreSQL and SQL Server), so a new database is fully set up on first start.

Notes:

- Primary keys are generated **client-side** (`ClientSideIdValueGenerator`): on first use per
  table it reads `MAX(Id)` and then increments in memory. This keeps the schema portable
  (no SQLite `AUTOINCREMENT` / PostgreSQL identity / SQL Server `IDENTITY` columns). One
  backend instance per database is the supported deployment model.
- The design-time migrations factory stays on SQLite (`dotnet ef migrations add`); the
  generated DDL is provider-neutral by construction.

## Contributing

1. Fork the repository
2. Create your feature branch
3. Commit your changes
4. Push to the branch
5. Open a Pull Request

## License

MIT

## Security Considerations

For Linux Docker staging, provider/secret configuration, trusted HTTPS proxies,
persistent documents, account bootstrap and coordinated restore, see
[DEPLOY.md](DEPLOY.md). Demo accounts are now seeded only in Development.
Optional Compose overrides support an existing Traefik network with generic
HTTPS/router settings, keeping host ports unpublished and proxy trust explicit.

New and changed passwords use versioned PBKDF2-SHA512 hashes with random salts
and 210,000 iterations. Existing refactor SHA256 hashes remain accepted for login;
FamilyPlanner BCrypt hashes are not supported. Harry confirmed two legacy users
and selected email password setup instead of importing their hashes on 2026-10-04.
Preserve the migrated accounts and their relationships, require password setup
before login, and deliver expiring one-use links through the existing SMTP service.
Email reset/setup is implemented and disabled by default; see [PASSWORD-RESET.md](PASSWORD-RESET.md)
for SMTP configuration, trusted links, limits and the staging verification gate.
It atomically revokes old sessions through the authentication version from
[#44](https://github.com/harryneufeld/qontract-backend/issues/44). Existing SMTP
delivery and production onboarding remain unverified in [#54](https://github.com/harryneufeld/qontract-backend/issues/54).

`AddRevocableAuthSessions` adds the session table and account authentication
version without changing existing password hashes, memberships or audit fields.
Every authenticated request verifies the persisted session ID, owner, account
version, expiry and revocation, in addition to account/tenant/membership activity.
Tokens without the new session/version claims are rejected: existing users must
sign in again after deployment. Tenant switching and invitation redemption reuse
the current session and its original expiry rather than extend it or create an
unrevocable replacement. Independent logins create independent sessions.

Apply the migration before starting the new backend and deploy all API instances
together; an older stateless instance would bypass revocation. Deploy the app
password-change signout update alongside it. If rolling back to the stateless
backend, rotate the JWT signing key across all instances so revoked tokens cannot
become usable again. Test rollout/restore against a copy first. Revocation rejects
subsequent authentication checks; it does not cancel requests already authorised.
Client logout clears local credentials even if the network request fails; server
revocation is only confirmed when the backend successfully processes logout.

Refresh tokens, short access-token lifetimes, session listing/revoke-all controls
and expired-session cleanup remain follow-ups. Password reset atomically updates
credentials and the same authentication version; TOTP challenges must also be
bound to it when that feature is implemented.

Invitation codes contain 32 random bytes, are stored only as SHA256 hashes, and
are redeemed atomically once. Optional email binding is case-insensitive. The
default expiry is seven days; the API accepts one to thirty days.

`AddRegistrationAndInvitations` backfills normalized account emails and enforces
uniqueness. Before upgrading an existing refactor database, check for duplicate
emails with `SELECT UPPER(TRIM("Email")), COUNT(*) FROM "Users" GROUP BY UPPER(TRIM("Email")) HAVING COUNT(*) > 1;`.
Resolve duplicates explicitly before migration; it will fail rather than merge
accounts or discard their access. Review non-ASCII email casing against the target
database's `UPPER` behavior, since new writes use .NET invariant normalization.

- Never commit your `appsettings.json` with real connection strings
- Use environment variables for sensitive data
- Implement proper input validation
- Add rate limiting to API endpoints
- Implement proper CORS policies
- Use HTTPS in production

### CORS origins in production (issue #29 review)

CORS behaviour (issue #12): in **Development** any origin is allowed (the
Flutter web dev server picks a random localhost port); in **Production** only
the origins listed in `Cors:Origins` are allowed, with credentials enabled.

- The app authenticates via a JWT `Authorization: Bearer` header (no
  cookies), and the native iOS/Android clients are not subject to CORS at
  all (no `Origin` header) — only the **web** deployment needs a matching
  origin.
- The `Cors:Origins` values in `appsettings.json` are localhost placeholders.
  When the web app gets a Linux Docker production host (qontract-app#34), add that origin
  (the scheme + host the browser will send, e.g. `https://web.qontract.example`)
  via indexed environment variables such as `Cors__Origins__0` or an environment-specific appsettings file —
  no code change is required. Until then the production list is fail-closed.
