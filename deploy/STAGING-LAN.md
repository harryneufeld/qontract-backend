# Native ARM64 / amd64 LAN staging

`compose.staging-lan.yml` is a standalone API, static web and Traefik stack for a
dedicated test host, including a Raspberry Pi 4 with **64-bit** Debian. It does
not modify an existing host web server. Choose a free HTTPS port (example 8443)
and bind it to the host's LAN address. Do not forward that port from the internet.
The API/web ports and Traefik dashboard are not published; Traefik uses a fixed
file provider, with no Docker socket mount.

The backend Dockerfile builds natively for the builder's architecture. Microsoft
.NET SDK/runtime and nginx images support ARM64. The current GHCR publication
workflow still publishes amd64 only; use this native build for ARM64 until that
workflow also verifies/publishes ARM64. The Pi's 64-bit kernel alone is insufficient:
check `getconf LONG_BIT` and `dpkg --print-architecture` as well.

Flutter web assets are architecture independent. Build them on the development
machine, not with the app's amd64 Flutter Docker build on the Pi:

```bash
# In qontract-app, using the actual staging LAN address:
flutter build web --release --base-href / --dart-define=QONTRACT_API_BASE_URL=https://STAGING_HOST:8443/api
```

Transfer only `build/web/` and the app's `deploy/nginx.conf` to the private staging
host, for example `/opt/qontract-staging/web` and `/opt/qontract-staging/nginx.conf`.
Keep databases/secrets outside that static directory. Build the ARM64 nginx image
using the precompiled assets as the build context:

```bash
# In the backend source checkout on the staging host:
docker build -f deploy/Dockerfile.web-assets -t qontract-web:staging /opt/qontract-staging/web
cp deploy/staging-lan.env.example deploy/staging-lan.env
```

Edit only the private env file: LAN hostname/IP, bind IP, free port, non-conflicting
Docker subnet and a reserved proxy IP within it. Ensure neither that subnet nor
the proxy IP collides with an existing host/VPN/container network. The fixed proxy
IP is the only trusted forwarded-header sender; do not trust an entire network.
Set dedicated local database and document bind directories, the copied nginx
configuration path and private API env-file path. Keep SQLite off network shares.

Create the data/document directories and allow the API image's `app` UID/GID to
write (currently 1654; verify after building). The Compose bind mounts refuse to
create missing paths. Create the private API env file with mode 600 containing
a newly generated `Jwt__Key`; never reuse production keys. Registration and SMTP
reset delivery default to disabled. Existing host services, users, volumes and
ports remain separate. Native builds can take several minutes on a Pi; leave
enough disk/RAM/swap for the SDK and containers.

```bash
docker compose --project-name qontract-staging --env-file deploy/staging-lan.env -f deploy/compose.staging-lan.yml config --quiet
docker compose --project-name qontract-staging --env-file deploy/staging-lan.env -f deploy/compose.staging-lan.yml build api
docker compose --project-name qontract-staging --env-file deploy/staging-lan.env -f deploy/compose.staging-lan.yml up -d --wait
docker compose --project-name qontract-staging --env-file deploy/staging-lan.env -f deploy/compose.staging-lan.yml ps
```

Traefik supplies its default self-signed certificate in this LAN template. The
browser will require explicit trust for this test host. This is not publicly
trusted HTTPS; use a trusted certificate/domain configuration before real user
onboarding. Do not disable certificate checks for real SMTP. If changing the
HTTPS port/address, rebuild the Flutter assets and update both API CORS and the
trusted reset URL. Real SMTP configuration/delivery checks remain a separate step
in [PASSWORD-RESET.md](../PASSWORD-RESET.md).

For an email rehearsal, add the `Smtp__*` settings from that guide to the existing
protected API env file, and set `PASSWORD_RESET_ENABLED=true` in the private
Compose env file. The template derives `PasswordReset__PublicAppUrl` from
`STAGING_HOST` and `STAGING_HTTPS_PORT`; keep these aligned with the Flutter
HTTPS URL. Recreate the API with the `up -d --wait` command
above; restarting an existing container does not load changed environment values.
Use a dedicated recipient account in the staging database and request a link
through Forgot password. Confirm actual inbox delivery, set a password, verify
one-time redemption and sign in. Keep passwords and reset links out of evidence.
The ARM64 rehearsal recorded in the password-reset guide passed these checks;
each deployment still needs validation with its own mail and hosting settings.

Start with empty staging storage or an explicitly reviewed **copied** fixture.
Never mount production/FamilyPlanner storage here. For a copied fixture, relocate
document paths to `/app/Storage`, invalidate restored sessions/reset records and
advance authentication versions before API startup. Retain source evidence and
do not guess ownership. See the [local rehearsal tooling](../tools/migration/README.md).

Verify all three native images' architecture, API/container readiness, browser
login, workspace privacy, checksum-identical document downloads, static assets and
the existing host service after startup. Record image IDs and source commits;
Compose startup alone is not proof of functional migration or SMTP delivery.
Back up database and documents together while the API is stopped. `down` keeps
bind data; never remove the staging data directories as a deployment step.

References: [Docker Debian ARM64 installation](https://docs.docker.com/engine/install/debian/)
and [Docker multi-platform builds](https://docs.docker.com/build/building/multi-platform/).
