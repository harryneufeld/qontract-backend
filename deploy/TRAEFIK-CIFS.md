# Backend staging with existing Traefik and host-mounted CIFS

`compose.traefik-cifs.yml` is standalone; do not combine it with the root Compose
file. It pulls the backend image from GHCR, joins your existing Traefik
network, routes HTTPS to container port 8080 and publishes no host ports.
It runs in Staging without demo accounts. No migration import is run by Compose;
the API applies its normal EF schema migrations on startup.

The example uses SQLite in a **local Docker named volume**. CIFS stores documents
only. For an existing PostgreSQL/SQL Server, set `DATABASE_PROVIDER` and replace
the connection string together; provide database networking, persistence and
backups separately. Use one API instance with SQLite. The existing SQLite
dependency advisory [#49](https://github.com/harryneufeld/qontract-backend/issues/49)
still needs resolution before a production release.

## 1. Configuration

Run commands from the backend repository root on your Linux Docker host:

```bash
cp deploy/traefik-cifs.env.example deploy/traefik-cifs.env
install -d -m 700 /opt/qontract/staging/secrets
install -m 600 deploy/traefik-cifs-api.env.example /opt/qontract/staging/secrets/api.env
openssl rand -hex 32
```

Put the generated key in `Jwt__Key` in the protected API env file. Replace the
example API/web hostnames, Traefik network, entrypoint, certificate resolver,
trusted proxy address, document path and `QONTRACT_IMAGE` in `traefik-cifs.env`.
Do not use the example signing key. The API file is read as a container env file;
only `deploy/traefik-cifs.env` is needed for Compose interpolation.

Traefik must already have the named TLS entrypoint/resolver and Docker provider.
Point API DNS at that proxy. If Traefik already supplies certificates without a
named resolver, remove the `tls.certresolver` label. Use Traefik's actual stable
IP on the configured Docker network for `TRUSTED_PROXY_IP`. This is the proxy's
address, not the network gateway or backend address. Never enable blanket
forwarded-header trust. Set `WEB_ORIGIN` to the eventual app origin, without a
path; the Flutter app will use `https://API_HOST/api`.

## 2. Prepare the mounted document directory

The CIFS share is mounted by the host, outside Compose. Do not put SMB credentials
in this stack. The backend runs as the image's non-root `app` user. Determine its
UID/GID after pulling, then configure CIFS permissions to allow that identity
to create, read, replace and remove documents. For the current .NET images this
is normally UID/GID 1654; verify it rather than changing the container to root.
Typical mount options include `uid=1654,gid=1654,file_mode=0600,dir_mode=0700`;
your NAS ACLs must allow writes as well. Keep the existing host mount policy.

For the example path, with `/mnt/nas` already mounted:

```bash
findmnt -T /mnt/nas -n -o FSTYPE | grep -qx cifs || exit 1
mkdir -p /mnt/nas/qontract-staging/documents
touch /mnt/nas/qontract-staging/documents/.qontract-storage-ready
```

Replace paths to match your settings. Create the marker **only after verifying
the CIFS mount**. Compose refuses to auto-create a missing bind source and the
API refuses to start without this marker. The marker does not prove the share
is writable or continuously available: test uploads/downloads, and arrange host
boot ordering so the CIFS mount is ready before Docker starts this stack. Stop
the API before deliberately unmounting the share. Avoid creating a matching
marker on the underlying local directory.

## 3. Publish, pull and start

Merge the GHCR workflow first and wait for the **Backend container** workflow on
`main` to finish. It runs the backend tests, builds the Linux amd64 Docker image
and checks container readiness/schema startup before publishing
`ghcr.io/harryneufeld/qontract-backend`. Pull requests only verify; they never
publish. Pushes to `main` publish the moving `main` tag; version tags such as
`v1.0.0` publish that exact version tag. Every publication also has a
`sha-FULL_COMMIT_SHA` tag. No `latest` tag is assumed.

To test an unmerged branch on staging, run the workflow manually (Actions →
**Backend container** → **Run workflow**, pick the branch). After the same
verification it publishes only `sha-FULL_COMMIT_SHA` for that branch; the
`main` and version tags are never moved by a manual run from another branch.

The workflow uses GitHub's built-in `GITHUB_TOKEN` with package-write permission;
no extra publishing password is required. A newly published GHCR package is
private by default and this workflow never changes visibility. If this package
already exists, confirm its visibility is Private in GitHub package settings
before publishing; existing visibility is preserved.

For the private image, give your server login access to this package and use a
GitHub PAT (classic) with `read:packages` (plus organization SSO authorization if
required). Authenticate interactively without putting the token in Compose/Git:

```bash
docker login ghcr.io --username YOUR_GITHUB_USERNAME
```

Paste the token at the password prompt. Existing packages may require granting
this repository Actions access in package settings. See
[GitHub's GHCR authentication guide](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry).

Use `:main` for initial staging, or copy the immutable `@sha256:...` reference
from the successful publish job summary into `QONTRACT_IMAGE` for a controlled
deployment. This workflow currently builds **Linux amd64**, suitable for x86-64
hosts; ARM hosts need an additional build target before deployment.

```bash
docker compose --project-name qontract-staging-api --env-file deploy/traefik-cifs.env -f deploy/compose.traefik-cifs.yml config --quiet
docker compose --project-name qontract-staging-api --env-file deploy/traefik-cifs.env -f deploy/compose.traefik-cifs.yml pull
docker compose --project-name qontract-staging-api --env-file deploy/traefik-cifs.env -f deploy/compose.traefik-cifs.yml run --rm --no-deps --entrypoint id qontract-api
docker compose --project-name qontract-staging-api --env-file deploy/traefik-cifs.env -f deploy/compose.traefik-cifs.yml up -d --wait
docker compose --project-name qontract-staging-api --env-file deploy/traefik-cifs.env -f deploy/compose.traefik-cifs.yml ps
curl --fail https://api.qontract-staging.example.com/health/ready
```

Use your API hostname in the last command. `config --quiet` avoids printing secrets.
The server needs only these deployment files and protected configuration; it does
not need the .NET SDK, source checkout or a local image build. `pull_policy: always`
refreshes the chosen tag/digest on `up`; it does not automatically update running
containers. Repeat `pull` and `up -d --wait` after choosing the next image. Back up
the paired database/documents before schema upgrades, and record the image digest.
The Dockerfile supplies the internal database readiness probe. Confirm HTTPS,
CORS, forwarded client IPs, and persistence with a real document upload/download
and container recreation on this host; readiness alone does not test CIFS.

Staging initially has no users; registration and password reset are disabled.
For manual testing, restrict access at Traefik, temporarily enable registration,
create your account/tenant with the app, then disable it and recreate the API.
Imported-account setup is a separate migration step. No SMTP configuration or
emails are enabled by this example.

Use a separate project name, env file, signing key, database and document folder
for production. A consistent backup includes the stopped SQLite volume, matching
CIFS documents and configuration. Never remove the database volume to update the
API. Follow [../DEPLOY.md](../DEPLOY.md) for restore/cutover requirements.

References: [Docker bind mounts](https://docs.docker.com/reference/compose-file/services/#volumes)
and [Traefik Docker routing](https://doc.traefik.io/traefik/reference/routing-configuration/other-providers/docker/).
