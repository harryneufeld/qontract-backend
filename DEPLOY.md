# Linux Docker deployment

Use separate Compose project names, volumes, public URLs,
JWT keys, database credentials and SMTP configuration for staging and production.

For a dedicated Raspberry Pi / ARM64 LAN test host, see
[native LAN staging](deploy/STAGING-LAN.md). It builds the API natively and serves
precompiled Flutter assets behind its own Traefik on a separate host port.

## Configuration and startup

For the standalone existing-Traefik / host-mounted-CIFS setup, see
[deploy/TRAEFIK-CIFS.md](deploy/TRAEFIK-CIFS.md) and
[deploy/compose.traefik-cifs.yml](deploy/compose.traefik-cifs.yml).
Its example keeps SQLite on a local Docker volume and documents on the CIFS bind
mount, runs in Staging and includes a storage-marker startup check.
This standalone file pulls from GHCR; see the guide for private-registry login,
the publishing workflow and pinned image references. The root Compose file below
continues to support local source builds.

The root Compose file runs the API in Production, binds port 5000 to host loopback
and persists `/app/data` and `/app/Storage` as named volumes. It no longer silently
pairs a SQL Server connection string with the default SQLite provider or creates
an SQL Server with a committed password. Choose the provider explicitly:

| `DATABASE_PROVIDER` | Connection string and persistence |
| --- | --- |
| `sqlite` | `Data Source=/app/data/QontractDb.sqlite`; API database volume stores the database. Use one API instance. |
| `postgresql` | Provision a reachable PostgreSQL database and restricted application user; configure its connection string and persistent storage/backups separately. |
| `sqlserver` | Provision a reachable SQL Server database and application user; configure its connection string, certificate trust and persistent storage/backups separately. |

PostgreSQL/SQL Server servers are not provisioned by this Compose file. Verify
runtime migrations on the chosen server before release; previous provider SQL
generation does not prove execution. A Docker-internal database service must share
an explicitly configured private network with the API. The database volume is used
only by SQLite; documents always use their own volume.

```bash
cp deploy/staging.env.example deploy/staging.env
# Edit provider, release tag, public URLs/hostnames, trusted proxy IP and secret path.
install -d -m 700 /opt/qontract/staging/secrets
install -m 600 deploy/api.env.example /opt/qontract/staging/secrets/api.env
# Edit that protected file: JWT signing key, matching connection string and SMTP.
docker compose --project-name qontract-staging-api --env-file deploy/staging.env --env-file /opt/qontract/staging/secrets/api.env config --quiet
docker compose --project-name qontract-staging-api --env-file deploy/staging.env --env-file /opt/qontract/staging/secrets/api.env build --pull
docker compose --project-name qontract-staging-api --env-file deploy/staging.env --env-file /opt/qontract/staging/secrets/api.env up -d --wait
```

Use `config --quiet`: full rendered Compose configuration can disclose secrets.
Environment injection keeps secrets out of images/Git. Both env-file arguments are required: the
protected file supplies required connection/JWT interpolation as well as runtime
SMTP settings. Missing values fail Compose validation instead of silently choosing
an unpersisted default database. Docker administrators can inspect container
environments. Use the host's secret-management controls and
restricted operator access. Do not add `debugSettings.json` to production images;
it loads after environment settings and could override deployment configuration.
The Docker build context excludes local env files, debug settings, databases,
storage and backups. The API runs as the image's non-root `app` user. Named volumes
inherit the prepared writable directories; pre-existing volumes/bind mounts may
need ownership adjustment before startup.

Production uses `deploy/production.env`, project name `qontract-production-api`
and a separate protected secrets file. The example publishes port 5001. Do not use
the staging volumes for production. Record exact app/backend commits, image tags,
provider/version, backup IDs and configuration version. Retain prior images;
pin image digests in the approved release environment for reproducible deployments.

## HTTPS proxy, client identity and readiness

### Optional existing Traefik integration

For a separately managed Traefik Docker provider, use Docker Compose 2.24.4 or
later and add `deploy/compose.traefik.yml` after the root file. No Traefik container
or Docker socket mount is added. The override removes the API's published host
port, keeps its private default network, and joins the existing external proxy
network. An explicit network label ensures Traefik selects the correct network.

Configure `TRAEFIK_NETWORK` (existing Docker network name), `TRAEFIK_ROUTER`
(unique router/service name), `TRAEFIK_HOST` (public API hostname without a scheme
or path) and `TRAEFIK_HTTPS_ENTRYPOINT` (existing TLS entrypoint). Match
`API_ALLOWED_HOSTS` to that API hostname, `WEB_ORIGIN` to the public web origin,
and the Flutter build's API URL to `https://API_HOST/api`. Examples contain only
generic domains/placeholders; actual settings belong in the local env files.

```bash
docker compose --project-name qontract-staging-api --env-file deploy/staging.env --env-file /opt/qontract/staging/secrets/api.env -f docker-compose.yml -f deploy/compose.traefik.yml config --quiet
docker compose --project-name qontract-staging-api --env-file deploy/staging.env --env-file /opt/qontract/staging/secrets/api.env -f docker-compose.yml -f deploy/compose.traefik.yml build --pull
docker compose --project-name qontract-staging-api --env-file deploy/staging.env --env-file /opt/qontract/staging/secrets/api.env -f docker-compose.yml -f deploy/compose.traefik.yml up -d --wait
```

Use the same file list for all lifecycle commands. Production needs its own env
file, secret path, project and router name. `API_PORT` is ignored in Traefik mode.
Both app and API use internal port 8080; the backend's Docker readiness check
continues to use its own loopback rather than a public Traefik route.

TLS is enabled. Use existing entrypoint/certificate-store configuration, or set
`TRAEFIK_CERT_RESOLVER` and append `-f deploy/compose.traefik-acme.yml` for a named
resolver already installed in Traefik. These files do not configure ACME or
HTTP-to-HTTPS redirects; those belong to the existing proxy deployment.

Set `TRUSTED_PROXY_IP` to Traefik's address on `TRAEFIK_NETWORK`, not the bridge
gateway or the API's address. Use a stable address managed in the proxy's own
network configuration. If Traefik is recreated with a different address, update
this setting and recreate the API. Do not broaden trust to every network peer.
Keep Traefik's forwarding trust restricted; a proxy chain before Traefik requires
its own reviewed forwarding configuration. In staging verify HTTPS scheme and
per-client-IP throttling, including spoofed forwarded headers. The existing
one-hop backend trust policy is unchanged by this optional Compose override.

The host-only rule assumes a dedicated API hostname and preserves `/api` paths.
For a shared hostname, adapt the route rule in your local override; do not strip
the API prefix. Actual Docker network reachability, certificate issuance and
forwarded-header behavior remain deployment checks on the target Linux host.

References: [Traefik Docker labels](https://doc.traefik.io/traefik/reference/routing-configuration/other-providers/docker/)
and [Compose merge/reset behavior](https://docs.docker.com/reference/compose-file/merge/).

### Host-installed proxy alternative

The existing host HTTPS proxy terminates TLS and forwards to `127.0.0.1:5000`
(production: 5001). For nginx, inside the existing HTTPS virtual host:

```nginx
location / {
    proxy_pass http://127.0.0.1:5000;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
    client_max_body_size 55m;
    proxy_read_timeout 120s;
}
```

Overwrite forwarded headers; do not accept client-supplied addresses as trusted.
`TRUSTED_PROXY_IP` is the immediate TCP peer as observed by the API, often the
Docker bridge gateway for a host proxy. Determine it from the actual host/network
and verify in staging; do not copy a guessed address or trust all networks. The
backend consumes at most one forwarded hop and trusts only explicitly configured
IP addresses. A containerized proxy needs private-network routing instead of the
host-loopback example. Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.

`API_ALLOWED_HOSTS` must match the public API hostname (multiple names use `;`).
Compose also permits `127.0.0.1` for the container's internal readiness probe.
`WEB_ORIGIN` is the browser's scheme and hostname, without a path, even when the
Flutter app is hosted under `/qontract/`. Compose replaces both localhost CORS
placeholders with that origin. `PUBLIC_APP_URL` includes the Flutter subpath and
must use HTTPS when password reset is enabled. The Flutter build must use the
public API URL, not the private service hostname.

`/health/ready` returns 200 when the database is reachable and 503 otherwise,
without account or database details. The image probes it over internal HTTP.
No HTTPS port is configured in the image; HTTPS is enforced at the public proxy.
The database is migrated at startup before requests are served. This probe is
exempt from request throttling; it does not verify storage writes or SMTP delivery.
Stop for failed migrations, unwritable storage or unhealthy containers. Deploy
one migration-owning instance first rather than race several instances.

## Accounts and SMTP

Demo users/tenants are seeded only in Development. Production and Staging start
empty; changing an existing deployment to this version does not delete previously
seeded accounts. Audit those accounts and explicitly remove/disable them before
public exposure if an older release created them. Do not use their known passwords.

For a fresh private staging environment, enable public registration briefly with
`ALLOW_PUBLIC_REGISTRATION=true`, create your own account and tenant, then set it
back to false and recreate the API container. Restrict the proxy to the operator
during bootstrap. Migrated accounts instead follow the agreed email setup flow;
the historical importer is not implemented yet. No production account or automated
administrator is provisioned by these deployment files.

Password reset remains disabled by default. Follow [PASSWORD-RESET.md](PASSWORD-RESET.md)
to configure existing SMTP, sender, mandatory TLS and the trusted public app URL,
then enable `PASSWORD_RESET_ENABLED=true`. Test actual delivery, single use,
expiry and browser URL scrubbing with a staging account before onboarding users.
Never store SMTP secrets in the Flutter build. Authenticator-based 2FA requires a persistent Data Protection key ring; see [TWO-FACTOR.md](TWO-FACTOR.md).

## Backup, restore and rollback

Before rehearsals and every API/schema release, stop the API and capture the
database and document volume at the same boundary. For SQLite, copy the entire
database volume while stopped, including any WAL/SHM files. For PostgreSQL/SQL
Server, use the provider's consistent native backup plus matching document storage.
Secure backups, manifests, secrets/configuration and SHA-256 checksums outside
public repos. A backup is not verified until restored into an isolated environment.

For an immutable **local SQLite migration target**, the migration tooling now
provides [offline backup/restore verification](tools/migration/README.md#offline-backup-and-restore-verification)
with database/document checksums, foreign-key checks and preserved import evidence.
This verifies a restored copy; it does not make that copy API-ready, relocate its
absolute document paths or replace the provider-specific deployment procedure below.

Restore the paired database/documents into new isolated volumes, use the matching
image/configuration, verify counts, relationships, document bytes and access, and
repeat after an interrupted import. Preserve source backups even for the approved
invoice/history exclusions. Never use `docker compose down --volumes` against data
you intend to keep; normal stop/down preserves named volumes.

Rollback means a retained image plus its compatible pre-upgrade database and
document snapshot; switching only the image after a migration is unsafe. Account
versions and consumed reset tokens can regress when restoring a database. Invalidate
restored reset records/sessions and advance authentication versions before reopening;
rotate the signing key across all instances when needed. See PASSWORD-RESET.md and
the README session-revocation rollout notes. Handle writes since the backup explicitly.

Remaining staging gates: Docker builds/startup on the actual Linux host, chosen
provider runtime verification, HTTPS/CORS/client-IP throttling, persistent documents,
SMTP delivery, coordinated restore and full app smoke checks. SQLite dependency
advisory [#49](https://github.com/harryneufeld/qontract-backend/issues/49) is addressed
by explicitly selecting `SQLitePCLRaw.bundle_e_sqlite3` 2.1.13 instead of EF's
older minimum. Local tests load SQLite 3.53.3 and restore audits report no vulnerable
packages. Deploy a rebuilt image to pick up the native library; previously running
instances still use their earlier binaries. Linux native loading/container startup
are checked by the existing PR workflow and remain host rollout checks.
No host deployment or production
migration has occurred as part of preparing these files.
