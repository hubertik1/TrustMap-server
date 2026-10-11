# TrustMap Server

TrustMap Server is the backend API for TrustMap, a social map for saving trusted places, reviews, dish notes and recommendations from friends. It replaces the app's earlier local or CloudKit-backed data model with a server-owned source of truth.

The API is built with ASP.NET Core, Entity Framework Core and SQLite. It handles Sign in with Apple, backend-issued JWT sessions, social graph workflows, review visibility, photo storage, push notifications and map/feed projections for the iOS and Mac Catalyst client.

## Features

- Sign in with Apple identity-token verification.
- Short-lived JWT access tokens with rotating refresh tokens.
- Active-user authorization and soft account deletion.
- Apple grant exchange and authorization revocation, with a durable account-deletion outbox for media and Apple retries. See [account deletion setup](docs/account-deletion.md).
- Friend requests, friendships and social visibility rules.
- Bilateral user blocking with a settings list for unblocking.
- One-tap review reports with copied evidence and moderation action links; private `.eml` files by default, optional SMTP delivery.
- Place reviews and dish reviews with `private`, `friends` and `public` visibility.
- Custom places, canonical place records and per-user category membership.
- Authenticated photo upload and protected media delivery.
- APNs push notification support with a no-op development fallback.
- Rate limits and daily quotas for abuse-sensitive operations.
- Health checks for liveness and database readiness.
- End-to-end integration tests against an in-memory SQLite database.

## Technology Stack

- Runtime: ASP.NET Core Web API on .NET 10
- Persistence: SQLite with Entity Framework Core
- Authentication: Sign in with Apple plus JWT bearer authentication
- Storage: local filesystem storage behind a storage abstraction
- Tests: xUnit integration tests using `WebApplicationFactory`
- Local orchestration: Docker Compose

## Repository Layout

```text
.
|-- docs/
|   |-- ios-integration.md
|   `-- push-notifications.md
|-- src/
|   |-- TrustMap.Api/              # Controllers, DTOs, API services, health checks
|   |-- TrustMap.Domain/           # Domain entities, enums and shared guards
|   `-- TrustMap.Infrastructure/   # EF Core, migrations, auth, storage and DI
|-- tests/
|   `-- TrustMap.IntegrationTests/ # HTTP-level integration tests
|-- docker-compose.yml
|-- Dockerfile
|-- global.json
`-- TrustMap.slnx
```

The solution intentionally stays small. There is no separate application layer yet because the current service boundaries are still simple enough to keep in the API and infrastructure projects.

## Domain Model

The core entities are:

- `User`
- `AppleIdentity`
- `RefreshToken`
- `FriendRequest`
- `Friendship`
- `Place`
- `PlaceReview`
- `DishReview`
- `Photo`
- `Category`
- `UserDeviceToken`
- `UserNotification`
- `QuotaUsage`

Important domain rules:

- A `Place` is a shared catalog record, not a private per-user object.
- A user can have at most one `PlaceReview` per `Place`.
- A `DishReview` belongs to a user and a place, and can optionally reference that user's own `PlaceReview`.
- A `Photo` belongs to exactly one review parent.
- Review visibility is enforced server-side.

## API Overview

Most endpoints require a valid TrustMap JWT. Public endpoints are limited to authentication and health checks.

Main endpoint groups:

- `/auth` for Apple sign-in, token refresh and logout.
- `/me` for current-user profile, privacy settings, avatar upload and account deletion.
- `/users` for user lookup and profile views.
- `/friends` and `/friend-requests` for the social graph.
- `/me/blocks` for blocking and unblocking, `/reports` for review reports.
- `/places` for place search and custom place management.
- `/place-reviews` and `/dish-reviews` for review workflows.
- `/photos` and `/media` for photo upload and protected media access.
- `/map` for map pin and map place projections.
- `/feed` for social activity.
- `/notifications` and `/me/device-tokens` for notification state and APNs tokens.
- `/health/live` and `/health/ready` for service health.

Swagger is enabled only in the `Development` environment.

## Local Development

### Prerequisites

- .NET SDK `10.0.201` or compatible
- Docker Desktop, optional but recommended for the fastest local start
- EF Core CLI tools if you want to create or apply migrations manually

Install EF tooling if needed:

```bash
dotnet tool install --global dotnet-ef
```

### Run With Docker Compose

```bash
docker compose up --build
```

The API listens on:

```text
http://localhost:5104
```

For a real iPhone on the same Wi-Fi network, use your Mac's LAN hostname or LAN IP:

```text
http://<your-mac-hostname>.local:5104
http://<your-lan-ip>:5104
```

Docker Compose stores the SQLite database and uploaded photos in Docker volumes.

### Run Directly On The Host

Apply migrations:

```bash
dotnet ef database update \
  --project src/TrustMap.Infrastructure/TrustMap.Infrastructure.csproj \
  --startup-project src/TrustMap.Api/TrustMap.Api.csproj
```

Start the API:

```bash
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/TrustMap.Api/TrustMap.Api.csproj --no-launch-profile
```

In development, the API binds to `http://0.0.0.0:5104` so physical devices on the same LAN can reach it.

Local host-run data is stored under:

```text
src/TrustMap.Api/App_Data/
src/TrustMap.Api/storage/
```

Both paths are ignored by Git.

## Configuration

Base settings live in:

```text
src/TrustMap.Api/appsettings.json
src/TrustMap.Api/appsettings.Development.json
```

Environment variables override appsettings using standard ASP.NET Core configuration names.

Important settings:

```text
ConnectionStrings__Database
Database__ApplyMigrationsOnStartup
Jwt__Issuer
Jwt__Audience
Jwt__SigningKey
Jwt__AccessTokenMinutes
Jwt__RefreshTokenDays
AppleAuth__Issuer
AppleAuth__AllowedAudiences__0
PhotoStorage__Provider
PhotoStorage__RootPath
PhotoStorage__RequestPath
PushNotifications__Provider
PushNotifications__Apns__Enabled
PushNotifications__Apns__TeamId
PushNotifications__Apns__KeyId
PushNotifications__Apns__BundleId
PushNotifications__Apns__PrivateKeyBase64
```

Development placeholder values are provided in `.env.example`, `docker-compose.yml` and `appsettings.Development.json`. Production must supply a strong `Jwt__SigningKey` through the hosting environment or a secret store. The default development signing key is rejected during production startup.

Example development configuration:

```text
ConnectionStrings__Database=Data Source=App_Data/trustmap.db;Foreign Keys=True;
Database__ApplyMigrationsOnStartup=true
Jwt__Issuer=TrustMap.Api
Jwt__Audience=TrustMap.iOS
Jwt__SigningKey=trustmap-local-development-signing-key-change-before-production
AppleAuth__AllowedAudiences__0=com.example.trustmap
PhotoStorage__Provider=local
PhotoStorage__RootPath=storage/photos
PhotoStorage__RequestPath=/uploads
```

## Apple Sign In

The backend validates Apple identity tokens against Apple's OpenID configuration and signing keys. `AppleAuth:AllowedAudiences` must include the bundle ID or service ID used by the client.

Apple signing keys are warmed in the background on process startup and shared with sign-in validation. Metadata waits are bounded, and temporary Apple outages return `503` rather than being treated as invalid identities. This improves first sign-in after an idle restart without requiring a paid hosting plan. See [startup and authentication on Azure Free F1](docs/startup-and-authentication.md) for limits and client-side follow-ups.

If the Apple identity token includes a `nonce` claim, the iOS client must send the original raw nonce in the sign-in request. The backend maps Apple `sub` values to local `AppleIdentity` records.

## Push Notifications

TrustMap supports APNs token-based authentication. Development can use the built-in no-op sender. Production APNs settings must be provided as environment variables or hosting-platform secrets.

The preferred Azure App Service format is:

```text
PushNotifications__Provider=apns
PushNotifications__Apns__Enabled=true
PushNotifications__Apns__TeamId=...
PushNotifications__Apns__KeyId=...
PushNotifications__Apns__BundleId=com.example.trustmap
PushNotifications__Apns__PrivateKeyBase64=...
```

See [docs/push-notifications.md](docs/push-notifications.md) for details.

## Blocking And Reporting

Reports are saved durably and prepared as private `.eml` files addressed to `hubert.mar@icloud.com`. The default mode requires no SMTP service and **does not deliver email to the recipient's inbox**. Files contain the reported review, photo previews and links to confirm ignore, removal or account suspension. Account suspension requires an administrator password configured on the server.

See [docs/blocking-and-reporting.md](docs/blocking-and-reporting.md) for storage paths, hosting settings, administrator password setup and optional SMTP delivery. Keep the moderation mail directory and Data Protection keys on persistent, private storage.

## Database Migrations

Create a migration:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/TrustMap.Infrastructure/TrustMap.Infrastructure.csproj \
  --startup-project src/TrustMap.Api/TrustMap.Api.csproj
```

Apply migrations:

```bash
dotnet ef database update \
  --project src/TrustMap.Infrastructure/TrustMap.Infrastructure.csproj \
  --startup-project src/TrustMap.Api/TrustMap.Api.csproj
```

## Testing

Run the full suite:

```bash
dotnet test TrustMap.slnx
```

The tests cover authentication, token refresh, logout, friend requests, friendships, privacy rules, map projections, feed projections, upload security, media delivery, notifications, quotas and account deletion.

## Deployment Notes

Before running in production:

- Set `ASPNETCORE_ENVIRONMENT=Production`.
- Use a strong `Jwt__SigningKey` from secret storage.
- Configure the real Apple bundle ID or service ID in `AppleAuth__AllowedAudiences`.
- Keep APNs private keys outside the repository.
- Keep SQLite data and uploaded files on persistent storage.
- Keep `Database__ApplyMigrationsOnStartup` enabled only if that matches your deployment strategy.
- Put the API behind HTTPS.
- Monitor rate-limit rejections, auth failures and upload volume.

## iOS Client Integration

The companion iOS and Mac Catalyst client reads its API base URL from build configuration. For local device testing, start this server and set the app's `TRUSTMAP_LAN_HOST` in `TrustMap-app/Config/Local.override.xcconfig`.

More integration details:

- [docs/ios-integration.md](docs/ios-integration.md)
- [docs/push-notifications.md](docs/push-notifications.md)

## Security Notes

Do not commit local databases, uploaded photos, APNs keys, `.env` files or production signing keys. The repository `.gitignore` excludes the standard local data paths used by the API.

Public knowledge of the API URL is expected for a mobile backend. Access control is enforced through JWT authentication, active-user checks, visibility rules, rate limits and quotas.
