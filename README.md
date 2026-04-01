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

- .NET 8.0 SDK
- SQL Server or PostgreSQL

### Installation

1. Restore dependencies:
```bash
dotnet restore
```

2. Update connection string in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=QontractDb;Trusted_Connection=False;User Id=your_user;Password=your_password"
  }
}
```

3. Run migrations:
```bash
dotnet ef database update --project Qontract.Infrastructure --startup-project Qontract
```

4. Build and run:
```bash
dotnet run --project Qontract.API --startup-project Qontract
```

5. API will be available at: `http://localhost:5000`

## API Endpoints

### Authentication
- `POST /api/Auth/login` - Login and get JWT token
- `POST /api/Auth/refresh-token` - Refresh JWT token

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

Open `http://localhost:5000/swagger` to view interactive API documentation.

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
