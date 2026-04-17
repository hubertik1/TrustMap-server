# TrustMap-server

Backend-first implementation for TrustMap. This repo replaces CloudKit/SwiftData as the source of truth with a REST API built on ASP.NET Core, SQLite and EF Core.

## Architecture

- Runtime: ASP.NET Core Web API on .NET 10
- Persistence: SQLite + EF Core 9
- Auth: Sign in with Apple as upstream identity provider, backend-issued JWT access tokens and rotating refresh tokens
- Storage: metadata in SQLite, photo blobs behind a storage abstraction with a local filesystem implementation for development
- Visibility: enforced server-side for `private`, `friends`, `public`
- Aggregates: dedicated `/feed` and `/map/places` endpoints so the iOS client does not have to reconstruct social projections locally

## Solution layout

- `src/TrustMap.Api`
  - controllers, DTOs, API-specific services, exception handling, health checks
- `src/TrustMap.Domain`
  - core entities and enums
- `src/TrustMap.Infrastructure`
  - EF Core, authentication helpers, photo storage, DI registration, migrations
- `tests/TrustMap.IntegrationTests`
  - end-to-end HTTP integration tests with SQLite in-memory and a fake Apple verifier

The structure is intentionally simple. There is no separate `Application` project because at this stage it would only add ceremony without reducing coupling.

## Core domain model

- `User`
- `AppleIdentity`
- `RefreshToken`
- `FriendRequest`
- `Friendship`
- `Place`
- `PlaceReview`
- `DishReview`
- `Photo`

Important simplifications:

- `Place` is a shared canonical catalog entry, not a private per-user record.
- One user can create at most one `PlaceReview` per `Place`.
- `DishReview` belongs to a user and place, and can optionally reference the user’s own `PlaceReview`.
- `Photo` must belong to exactly one review parent.

## Local development

### 1. Start API with Docker Compose

```bash
docker compose up --build
```

Services:

- API: [http://localhost:8080](http://localhost:8080)
- SQLite database file inside the container: `/app/App_Data/trustmap.db`

### 2. Run API directly from the host

```bash
dotnet ef database update \
  --project src/TrustMap.Infrastructure/TrustMap.Infrastructure.csproj \
  --startup-project src/TrustMap.Api/TrustMap.Api.csproj

dotnet run --project src/TrustMap.Api/TrustMap.Api.csproj --no-launch-profile
```

This creates the local SQLite file at `src/TrustMap.Api/App_Data/trustmap.db` if it does not exist yet.

## Configuration

Main settings live in:

- `src/TrustMap.Api/appsettings.json`
- `src/TrustMap.Api/appsettings.Development.json`

Environment variables override appsettings using standard ASP.NET naming.

### Required settings

- `ConnectionStrings__Database`
- `Jwt__Issuer`
- `Jwt__Audience`
- `Jwt__SigningKey`
- `AppleAuth__AllowedAudiences__0`
- `PhotoStorage__Provider`
- `PhotoStorage__RootPath`
- `PhotoStorage__RequestPath`

### Example values

```text
ConnectionStrings__Database=Data Source=App_Data/trustmap.db;Foreign Keys=True;
Jwt__Issuer=TrustMap.Api
Jwt__Audience=TrustMap.iOS
Jwt__SigningKey=trustmap-local-development-signing-key-change-before-production
AppleAuth__AllowedAudiences__0=com.your.bundle.id
PhotoStorage__Provider=local
PhotoStorage__RootPath=storage/photos
PhotoStorage__RequestPath=/uploads
```

## Migrations

The initial migration already exists in `src/TrustMap.Infrastructure/Persistence/Migrations`.

Create a new migration:

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

## Health checks

- `GET /health/live`
  - process liveness only
- `GET /health/ready`
  - validates database connectivity

## Testing

Run the full suite:

```bash
dotnet test TrustMap.slnx
```

Covered flows:

- Apple auth exchange
- refresh/logout
- friend request send/accept/reject/cancel
- list friends
- place review CRUD
- dish review CRUD
- feed visibility
- map visibility

## Apple auth notes

- The backend verifies Apple identity tokens using Apple’s OpenID configuration and signing keys.
- `AppleAuth:AllowedAudiences` must contain the real iOS bundle ID or Apple Service ID used by TrustMap.
- If the Apple token contains a `nonce` claim, the client must also send the original `rawNonce`.
- `authorizationCode` is accepted by contract for future extensibility, but the current implementation authenticates by validating the Apple identity token and mapping `sub` to `AppleIdentity`.

## API and iOS integration guide

See:

- [docs/ios-integration.md](/Users/hubert/Projects/TrustMap/Trustmap-server/docs/ios-integration.md)
