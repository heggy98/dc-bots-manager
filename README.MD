# Discord Bot Alliance

A full-stack Discord bot management platform with:

- ASP.NET Core backend API (bot lifecycle, auth, logging, plugins)
- Discord runtime services and plugin-based command system
- Angular frontend admin dashboard
- SQL Server / LocalDB persistence via Entity Framework Core

## Repository Structure

- `BotManager.sln` - main solution
- `BotManager.Backend.API` - ASP.NET Core API host (entry point in `Program.cs`)
- `BotManager.Backend.Bots` - Discord runtime and bot services
- `BotManager.Backend.Bots.Tests` - backend bot runtime tests
- `BotManager.Backend.API.Tests` - API unit tests
- `BotManager.Backend.API.IntegrationTests` - API integration tests against SQL Server (Testcontainers, needs Docker)
- `BotManager.Backend.Services` - business services
- `BotManager.Backend.Entities` - EF Core entities and migrations
- `BotManager.Backend.Shared` - shared models/services
- `BotManager.Frontend` - Angular 21 admin UI

## Tech Stack

- .NET 10
- ASP.NET Core Web API + SignalR
- Entity Framework Core (SQL Server)
- Serilog (+ SQL sink)
- Discord.Net runtime integration
- Angular 21

## Prerequisites

- .NET SDK 10.x
- Node.js 20+ and npm
- SQL Server LocalDB or SQL Server instance
- (Optional) Visual Studio 2022 / VS Code

## Quick Start (Local Development)

### 1) Clone and restore

```bash
git clone <your-repo-url>
cd discord-bot-aliance
dotnet restore BotManager.sln
```

### 2) Install frontend dependencies

```bash
cd BotManager.Frontend
npm install
cd ..
```

### 3) Configure secrets (important)

Do **not** keep real credentials in tracked files.
Use one of these approaches:

- `dotnet user-secrets` for local development
- environment variables in your shell/CI/CD
- untracked local override files

Minimum required settings for backend:

- `ConnectionStrings:DefaultConnection`
- `JwtSettings:Secret` (random, at least 32 bytes; the app refuses to start otherwise)
- `JwtSettings:Issuer`
- `JwtSettings:Audience`
- `AdminCredentials:Email`
- `AdminCredentials:PasswordHash` (BCrypt hash, see below). A plaintext `AdminCredentials:Password` is accepted **only** in the `Development` environment.
- `GoogleAuth:ClientId`
- `GoogleAuth:ClientSecret`

The app fails at startup when the admin credentials are missing (no login is possible without them).

Generate the admin password hash:

```bash
dotnet run --project BotManager.Backend.API/BotManager.Backend.API.csproj -- --hash-password "<admin-password>"
```

Optional settings:

- `JwtSettings:ExpiryMinutes` – access token lifetime (default 15; sessions are extended with refresh tokens)
- `Auth:RefreshTokenDays` – refresh token lifetime (default 14)
- `Auth:CookieSecure` – `Secure` flag of the auth cookies (default `true`; set `false` only for plain `http://` outside localhost)
- `Recaptcha:SiteKey` / `Recaptcha:SecretKey` / `Recaptcha:RequiredAfterFailedAttempts` – optional reCAPTCHA v2 on login after N failed attempts (default 3)
- `Bots:AutoStartEnabled` – start bots marked "auto-start" when the API starts (default `true`)
- `RateLimiting:AuthPermitLimit` / `RateLimiting:PublicPermitLimit` – requests per minute per IP (defaults 10 / 60)
- `ReverseProxy:KnownNetworks` – trusted proxy networks in CIDR notation
- `Cors:AllowedOrigins` – allowed frontend origins (default `http://localhost:4200`, `https://localhost:4200`)
- `ReverseProxy:KnownProxies` – IPs of trusted reverse proxies; only these may set `X-Forwarded-For`
- `LogRetention:SystemLogsDays` / `CommandUsageDays` / `LoginAuditDays` – log retention (defaults 30 / 90 / 90)
- `Alerts:DiscordWebhookUrl` – Discord webhook that receives alerts when a bot goes down (marked Offline after the
  30 s disconnect grace period, or a failed start) and when it is back online
- `Alerts:Smtp:Host` / `Port` / `User` / `Password` / `From` / `To` / `UseSsl` – the same alerts by e-mail (STARTTLS,
  default port 587; `To` may contain several comma-separated addresses)
- `Alerts:DebounceMinutes` – at most one down alert per bot within this window (default 10); a "back online" alert is
  only sent after a delivered down alert, so a flapping bot cannot spam. `Alerts:NotifyRecovery` (default `true`)
  toggles recovery alerts. Alerts never contain tokens; delivery failures are only logged.
- `Metrics:Enabled` – serves Prometheus metrics at `/metrics` (default `false`; `true` in docker-compose). Only direct,
  non-proxied requests are answered (requests carrying `X-Forwarded-*` get 404), or – when `Metrics:ApiKey` is set – only
  requests with `Authorization: Bearer <key>`. nginx and Caddy never expose `/metrics`.
- `OpenTelemetry:OtlpEndpoint` / `OtlpProtocol` (`grpc` or `http/protobuf`) / `ServiceName` – export traces
  (ASP.NET Core, HttpClient, SQL) and metrics to an OTLP collector. Discord webhook/interaction URLs are never traced.
- `DataProtection:CertificatePath` / `DataProtection:CertificatePassword` – certificate used to encrypt the
  Data Protection key ring. Keys are stored in the database (`DataProtectionKeys`); on Windows they are protected
  with DPAPI automatically, on Linux/containers configure a certificate.

Example (recommended): use .NET User Secrets for local readable storage.

```bash
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "JwtSettings:Secret" "<your-strong-jwt-secret>"
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "AdminCredentials:Email" "admin@example.com"
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "AdminCredentials:PasswordHash" "<bcrypt-hash-from---hash-password>"
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "GoogleAuth:ClientId" "<google-client-id>"
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "GoogleAuth:ClientSecret" "<google-client-secret>"
```

To view what is saved locally:

```bash
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj list
```

Windows local file location for these secrets:

- `%APPDATA%\\Microsoft\\UserSecrets\\botmanager-backend-api-9b2f9a0a-5e8c-4e18-a4b6-2e8d83f0bbd2\\secrets.json`

Second computer setup:

- copy the values securely (not via public/shared channels)
- run the same `dotnet user-secrets ... set ...` commands on the second machine
- or copy `%APPDATA%\\Microsoft\\UserSecrets\\botmanager-backend-api-9b2f9a0a-5e8c-4e18-a4b6-2e8d83f0bbd2\\secrets.json` to the same path on the second machine (secure transfer only)
- avoid copying tracked `appsettings.json` with real values

### 4) Run backend

```bash
dotnet run --project BotManager.Backend.API/BotManager.Backend.API.csproj
```

Default local URLs (from launch settings):

- `https://localhost:7165`
- `http://localhost:5165`

### 5) Run frontend

In a separate terminal:

```bash
cd BotManager.Frontend
npm start
```

Frontend runs at `http://localhost:4200` and proxies `/api` + `/hubs` to backend (`https://localhost:7165`).

## Build and Test

### Docker (whole stack)

```bash
cp .env.example .env   # fill in the secrets
docker compose up -d --build
```

The app runs at `http://localhost:8080` (nginx serves the SPA and proxies `/api` and `/hubs` to the API on the same
origin). Health: `GET /health` (API incl. database), `GET /health/live`.

### Monitoring (Prometheus + Grafana)

```bash
# .env: GRAFANA_ADMIN_PASSWORD=<strong password>, METRICS_ENABLED=true (default)
docker compose --profile monitoring up -d
```

Prometheus scrapes `api:8080/metrics` on the internal network (not published); Grafana runs on `http://127.0.0.1:3000`
with a provisioned Prometheus datasource and the "BotManager overview" dashboard. Custom metrics (meter `BotManager`):

| Metric | Type | Labels |
| --- | --- | --- |
| `bots_running` | gauge | – |
| `gateway_latency_ms` | gauge | `bot_id` |
| `commands_executed_total` | counter | `command`, `success` |
| `bot_disconnects_total` | counter | `bot_id` |
| `alerts_sent_total` | counter | `kind`, `channel`, `success` |

plus ASP.NET Core (`http_server_request_duration_seconds`), HttpClient and .NET runtime metrics.

### HTTPS with Caddy (Let's Encrypt)

For a public deployment, `docker-compose.https.yml` adds a [Caddy](https://caddyserver.com) reverse proxy that
terminates TLS with an automatically obtained and renewed Let's Encrypt certificate:

```bash
# .env: DOMAIN=bots.example.com, ACME_EMAIL=you@example.com (DNS of DOMAIN -> this host, ports 80/443 open)
docker compose -f docker-compose.yml -f docker-compose.https.yml up -d --build
```

- The app is served at `https://${DOMAIN}`; http (port 80) redirects to https. Caddy sends HSTS
  (`Strict-Transport-Security: max-age=31536000`).
- The override forces `Auth__CookieSecure=true` and no longer publishes the plain `8080` port, so nginx is reachable only
  through Caddy. Without the override the stack keeps working on `http://localhost:8080` as before.
- Proxy chain: client → Caddy (`172.29.0.10` on the `edge` network) → nginx → API. nginx trusts `X-Forwarded-For` /
  `X-Forwarded-Proto` only from Caddy's fixed address (`set_real_ip_from`), resolves the real client IP and forwards it to
  the API as a single `X-Forwarded-For` entry; the API trusts only nginx (`ReverseProxy__KnownNetworks__0=172.28.0.0/16`),
  so login lockouts and rate limits apply to the real client IP.
- Certificates and ACME account are stored in the `caddy-data` volume (keep it to avoid Let's Encrypt rate limits).
- Set `COMPOSE_FILE=docker-compose.yml:docker-compose.https.yml` in `.env` to use plain `docker compose` commands.

### Build solution

```bash
dotnet build BotManager.sln
```

### Run backend tests

```bash
dotnet test BotManager.sln                                                    # all tests (integration tests need Docker)
dotnet test BotManager.sln --filter "FullyQualifiedName!~IntegrationTests"    # unit tests only
```

CI (GitHub Actions, `.github/workflows/ci.yml`) runs the build, unit and integration tests, frontend build/tests,
dependency audits and Docker image builds on every push and pull request. Dependabot keeps dependencies up to date.

### Frontend tests

```bash
cd BotManager.Frontend
npm test
```

## Runtime Notes

- Bot command model uses plugin architecture (`discord-board` plugin).
- Several bots can run at the same time; each has its own Discord gateway session. Bots marked "auto-start"
  are started automatically after the API starts.
- Authentication uses HttpOnly cookies: a short-lived JWT access cookie and a rotating refresh cookie. Reusing an
  old refresh token revokes all sessions; "Log out everywhere" in the admin UI ends every session on all devices.
  Cookie-authenticated write requests must send `X-Requested-With: BotManager` (the SPA does this automatically).
- Team roles are bound to Discord roles by id (`Teams.RoleId`); the board never grants roles with elevated permissions.
- Bot tokens are encrypted with ASP.NET Core Data Protection. If the key ring is lost, stored tokens cannot be
  decrypted and must be re-entered in the admin UI.
- API startup applies EF migrations automatically.
- SignalR hub endpoint: `/hubs/bot-events`.
- Repository runtime is based on direct project commands (`dotnet run` and `npm start`), not a Procfile.

For deeper runtime internals, see:

- `DEVELOPER_RUNTIME_FLOWS.md`
- `PROGRAM_CS_DOCUMENTATION.md`

