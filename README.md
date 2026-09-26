# Qontract Backend - Multi-tenant Contract Management System

A robust backend API for managing contracts and their associated documents with multi-tenant architecture and clean architecture principles.

## Features

- ✅ Multi-tenant data model (Family & Company tenants)
- ✅ Clean Architecture with Domain/Application/Infrastructure separation
- ✅ Entity Framework Core with migration support
- ✅ File upload/download for contract documents
- ✅ CRUD operations for all entities
- ✅ JWT-based authentication and authorization
- ✅ Soft delete for audit trails
- ✅ Versioning for document uploads

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
   > `docker compose` expects the host variable `JWT_KEY` for the same reason.

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

5. A seed tenant (`Test Family`) and admin user are created on first run:
   - Email: `admin@qontract.com`
   - Password: `password123`

> Note: the Flutter app expects the API at `http://localhost:65533/api` and
> the web client on `http://localhost:3000` (CORS).

## API Endpoints

### Authentication
- `POST /api/Auth/login` - Login and get JWT token

### Tenants
- `GET /api/Tenants` - List all tenants
- `GET /api/Tenants/{id}` - Get tenant details
- `POST /api/Tenants` - Create new tenant
- `PUT /api/Tenants/{id}` - Update tenant
- `PATCH /api/Tenants/{id}/deactivate` - Deactivate tenant

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

### Contracts
- `GET /api/Contracts` - List all contracts
- `GET /api/Contracts/{id}` - Get single contract
- `POST /api/Contracts` - Create new contract
- `PUT /api/Contracts/{id}` - Update contract
- `PATCH /api/Contracts/{id}/deactivate` - Deactivate contract
- `DELETE /api/Contracts/{id}` - Soft delete contract

### Documents
- `GET /api/Contracts/{contractId}/documents` - List documents for a contract
- `POST /api/Contracts/{contractId}/documents` - Upload document
- `GET /api/Documents/{id}` - Get document metadata
- `GET /api/Documents/{id}/download` - Download document
- `PUT /api/Documents/{id}` - Update document
- `DELETE /api/Documents/{id}` - Delete document

### Addresses
- `POST /api/Addresses` - Create address
- `PUT /api/Addresses/{id}` - Update address
- `GET /api/Addresses/{id}` - Get address
- `PATCH /api/Addresses/{id}/deactivate` - Deactivate address

## Development

### Running with Docker

```bash
docker-compose up --build
```

### Testing

```bash
dotnet test
```

### Swagger UI

Open `https://localhost:65532/swagger` (or `http://localhost:65533/swagger`) to view interactive API documentation.

## Contributing

1. Fork the repository
2. Create your feature branch
3. Commit your changes
4. Push to the branch
5. Open a Pull Request

## License

MIT

## Security Considerations

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
  When the web app gets a production host (qontract-app#31), add that origin
  (the scheme + host the browser will send, e.g. `https://web.qontract.example`)
  via `ASPNETCORE_Cors__Origins` or an environment-specific appsettings file —
  no code change is required. Until then the production list is fail-closed.
