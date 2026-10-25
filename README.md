# qontract backend

**The self-hostable server behind qontract — the privacy-first manager for contracts, subscriptions and insurance policies.**

qontract helps households and small businesses keep every contract in one place,
with its documents, price history, end dates and real costs. This repository is
the .NET 10 REST API that stores contracts, contractors and documents for the
[qontract app](https://github.com/harryneufeld/qontract-app) on Android, iOS and
the web.

## Why qontract

- **Document import with OCR and one-step contract assignment.** The app recognises
  letters and PDFs on the phone and suggests the matching contract; the backend
  provides the contract lookup and stores the original file against the chosen
  contract in a single save. Recognised text stays on the device.
- **Your data, your server.** Open source under the AGPL-3.0. Run it with Docker
  on SQLite (zero setup) or PostgreSQL, and keep documents on local disk, S3 or
  Azure Blob Storage.
- **Personal and shared workspaces.** Owner-only personal contracts and documents
  next to shared household or company workspaces, with admin/member roles,
  invitation codes and controlled public registration.
- **Security built in.** PBKDF2-SHA512 password hashing, revocable server-side
  sessions, authenticator-app two-factor authentication, one-use email password
  reset links and fail-fast configuration for production secrets.

## Features

- Contracts with billing periods, dated price history, currencies, IBAN, notes,
  business types, responsible members and a restorable trash
- Contractors, private account addresses and shared workspace addresses
- Document upload with versioning, shared links across contracts and private or
  shared file libraries
- JWT authentication with tenant switching, 2FA (TOTP) and SMTP password reset
- English and German API messages via `Accept-Language`
- Clean Architecture (Domain / Application / Infrastructure / API) with
  EF Core migrations and an integration test suite

## Getting started

Requirements: the .NET 10 SDK. No external database is needed for development —
SQLite is created and migrated automatically on first start.

```bash
dotnet restore
dotnet run --project Qontract.API
```

The API listens on `http://localhost:65533` (Swagger UI at `/swagger`). In
Development, demo data is seeded with the account `admin@qontract.com` /
`password123`. Demo accounts are never created in Staging or Production.

Run the tests with:

```bash
dotnet test
```

Outside Development the API refuses to start without a signing key; provide one
via `Jwt__Key`, for example `Jwt__Key="$(openssl rand -base64 48)"`.

## Self-hosting

The Docker Compose setup runs the API in Production mode behind an HTTPS reverse
proxy, with persistent document storage and secrets in a protected env file.
Optional overrides integrate with an existing Traefik network.

| Setting | Options |
|---------|---------|
| `Database__Provider` | `sqlite` (default), `postgresql`, `sqlserver` |
| `Storage__Provider` | `local` (default), `s3`, `azure` |
| `Registration__AllowPublic` | `false` (default) — invitation-only signup |
| `Registration__RequireEmailConfirmation` | `false` (default) — `true` activates accounts only after an emailed link |
| `Terms__Version`, `Terms__Url` | empty (default) — set both to require accepting terms of use |
| `Cors__Origins__0`, … | allowed origins for the web app |
| `Auth__Sessions__AccessTokenMinutes` | `15` (default) — lifetime of the access token; sessions are kept alive by refresh tokens |
| `Auth__Sessions__Web__IdleDays`, `…__AbsoluteDays` | `30` / `90` (default) — web session lifetimes |
| `Auth__Sessions__Mobile__IdleDays`, `…__AbsoluteDays` | `90` / `365` (default) — mobile session lifetimes |

**The web app and the API must be on the same site** — for example `app.example.com`
and `api.example.com`, both under `example.com`. The web app keeps its refresh token in an
`HttpOnly; Secure; SameSite=Strict` cookie, and browsers that block third-party cookies will
not send it to an API on a different site, so users would be asked to sign in again
after every reload. Mobile apps are not affected. See
[Sessions](docs/IMPLEMENTATION-NOTES.md#sessions-and-refresh-tokens).

See [DEPLOY.md](DEPLOY.md) for the full guide, including backups and restore.

PostgreSQL is the recommended server database. It has its own migration chain
with native column types, and an existing SQLite database moves over with a
built-in copy command — see [DEPLOY.md](DEPLOY.md#moving-from-sqlite-to-postgresql).

## Project structure

```
Qontract.Domain/          entities and domain logic
Qontract.Application/     DTOs, validators and services
Qontract.Core/            shared security and storage abstractions
Qontract.Infrastructure/  EF Core, migrations, file storage
Qontract.Infrastructure.PostgreSql/  PostgreSQL migration chain
Qontract.API/             controllers, authentication, startup
Qontract.Tests/           integration and unit tests
```

## Documentation

- [Implementation notes](docs/IMPLEMENTATION-NOTES.md) — API endpoints, database providers, security details
- [DEPLOY.md](DEPLOY.md) — Docker deployment, secrets and restore
- [TWO-FACTOR.md](TWO-FACTOR.md) — authenticator-app 2FA
- [PASSWORD-RESET.md](PASSWORD-RESET.md) — SMTP password reset
- [CONTRACT-LIFECYCLE.md](CONTRACT-LIFECYCLE.md) — contract states and trash
- [NOTIFICATIONS.md](NOTIFICATIONS.md) — recommendations, reminders and their settings
- [LEGACY-MAPPING.md](LEGACY-MAPPING.md) — migration from the legacy system

## Contributing

Issues and pull requests are welcome. Please open an issue to discuss larger
changes first. Changes to `master` go through pull requests; run `dotnet test`
before submitting.

## License

Copyright © 2026 Harry Neufeld.

qontract is free software: you can redistribute it and/or modify it under the
terms of the [GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0). If you
run a modified version as a network service, the AGPL requires you to offer its
users the corresponding source code.
