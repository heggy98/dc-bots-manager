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
- `BotManager.Backend.Services` - business services
- `BotManager.Backend.Entities` - EF Core entities and migrations
- `BotManager.Backend.Shared` - shared models/services
- `BotManager.Frontend` - Angular 19 admin UI

## Tech Stack

- .NET 10
- ASP.NET Core Web API + SignalR
- Entity Framework Core (SQL Server)
- Serilog (+ SQL sink)
- Discord.Net runtime integration
- Angular 19

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
- `JwtSettings:Secret`
- `JwtSettings:Issuer`
- `JwtSettings:Audience`
- `AdminCredentials:Email`
- `AdminCredentials:Password`
- `GoogleAuth:ClientId`
- `GoogleAuth:ClientSecret`
- any Discord bot token keys

Example (recommended): use .NET User Secrets for local readable storage.

```bash
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "JwtSettings:Secret" "<your-strong-jwt-secret>"
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "AdminCredentials:Email" "admin@example.com"
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "AdminCredentials:Password" "<admin-password>"
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "GoogleAuth:ClientId" "<google-client-id>"
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "GoogleAuth:ClientSecret" "<google-client-secret>"
dotnet user-secrets --project BotManager.Backend.API/BotManager.Backend.API.csproj set "DCBODALIANCEBOTKEY" "<discord-bot-token>"
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

### Build solution

```bash
dotnet build BotManager.sln
```

### Run backend tests

```bash
dotnet test BotManager.Backend.Bots.Tests/BotManager.Backend.Bots.Tests.csproj
```

### Frontend tests

```bash
cd BotManager.Frontend
npm test
```

## Runtime Notes

- Bot command model uses plugin architecture (`discord-board` plugin).
- API startup applies EF migrations automatically.
- SignalR hub endpoint: `/hubs/bot-events`.
- Repository runtime is based on direct project commands (`dotnet run` and `npm start`), not a Procfile.

For deeper runtime internals, see:

- `DEVELOPER_RUNTIME_FLOWS.md`
- `PROGRAM_CS_DOCUMENTATION.md`

## Security Checklist for GitHub

Before pushing:

- Remove real credentials/tokens from tracked files.
- Rotate any token/secret that was ever committed.
- Ensure build artifacts are ignored and not tracked.
- Prefer secrets from environment/user-secrets at runtime.

Recommended commands:

```bash
git grep -n -I -E "Secret|Password|ClientSecret|BotToken|JwtSettings|AdminCredentials|ConnectionStrings"
```

