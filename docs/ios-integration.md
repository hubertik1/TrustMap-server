# TrustMap iOS Integration Guide

This document defines the backend contract that `Trustmap-app` should integrate with after the backend-first migration.

## Auth flow

### Sign in with Apple -> backend session

1. iOS starts Sign in with Apple via `AuthenticationServices`.
2. iOS generates a `rawNonce`, hashes it for `ASAuthorizationAppleIDRequest.nonce`, and keeps the raw value.
3. iOS receives:
   - `identityToken`
   - `authorizationCode`
   - Apple user identifier
   - optional full name and email on first login
4. iOS calls `POST /auth/apple`.
5. Backend verifies the Apple identity token, maps Apple `sub` to a local user, and returns:
   - `accessToken`
   - `accessTokenExpiresAtUtc`
   - `refreshToken`
   - `refreshTokenExpiresAtUtc`
   - current user profile
6. iOS stores the access token and refresh token in Keychain.

### Refresh flow

1. When the API returns `401` because the access token expired, iOS calls `POST /auth/refresh`.
2. Backend rotates the refresh token and returns a new access token and refresh token pair.
3. iOS replaces both tokens in Keychain.

### Logout flow

1. iOS calls `POST /auth/logout` with the current refresh token.
2. Backend revokes the refresh token.
3. iOS clears all session state and Keychain tokens.

## Endpoint summary

The API returns relative media paths such as `/uploads/...`. The iOS app resolves them against its configured `API_BASE_URL`, which now comes from the active `Local` or `Prod` scheme instead of hardcoded source values.

### Auth and profile

- `POST /auth/apple`
- `POST /auth/refresh`
- `POST /auth/logout`
- `GET /me`
- `PATCH /me`

### Users

- `GET /users/{id}`
- `GET /users/search?q=`

### Friends

- `POST /friend-requests`
- `GET /friend-requests/incoming`
- `GET /friend-requests/outgoing`
- `POST /friend-requests/{id}/accept`
- `POST /friend-requests/{id}/reject`
- `POST /friend-requests/{id}/cancel`
- `GET /friends`
- `DELETE /friends/{userId}`

### Places

- `GET /places`
- `GET /places/{id}`
- `GET /places/search?q=`
- `POST /places`

### Place reviews

- `GET /place-reviews`
- `GET /place-reviews/{id}`
- `POST /place-reviews`
- `PATCH /place-reviews/{id}`
- `DELETE /place-reviews/{id}`

### Dish reviews

- `GET /dish-reviews`
- `GET /dish-reviews/{id}`
- `POST /dish-reviews`
- `PATCH /dish-reviews/{id}`
- `DELETE /dish-reviews/{id}`

### Photos

- `POST /photos/upload`
- `DELETE /photos/{id}`

### Aggregates

- `GET /feed`
- `GET /map/places`

## Example requests and responses

### `POST /auth/apple`

Request:

```json
{
  "identityToken": "<apple-identity-token>",
  "authorizationCode": "<apple-authorization-code>",
  "rawNonce": "<raw-nonce-used-by-ios>",
  "email": "user@privaterelay.appleid.com",
  "displayName": "Hubert",
  "preferredHandle": "hubert"
}
```

Response:

```json
{
  "accessToken": "<jwt>",
  "accessTokenExpiresAtUtc": "2026-04-07T12:00:00Z",
  "refreshToken": "<refresh-token>",
  "refreshTokenExpiresAtUtc": "2026-05-07T12:00:00Z",
  "user": {
    "id": "0cf0c39d-2de0-78cb-8d20-9435ba82ab5d",
    "handle": "hubert",
    "displayName": "Hubert",
    "bio": null,
    "avatarUrl": null,
    "relationshipStatus": "self",
    "friendCount": 0,
    "visiblePlaceReviewCount": 0,
    "visibleDishReviewCount": 0,
    "isMe": true
  }
}
```

### `POST /place-reviews`

Request:

```json
{
  "placeId": "f69cf7f9-d7a6-7ca8-8a03-c940f4f49575",
  "visibility": "friends",
  "rating": 5,
  "title": "Worth the detour",
  "body": "Excellent coffee and very consistent brunch menu."
}
```

Response:

```json
{
  "id": "f39ad1eb-562d-7855-9246-b499e775f11e",
  "placeId": "f69cf7f9-d7a6-7ca8-8a03-c940f4f49575",
  "visibility": "friends",
  "rating": 5,
  "title": "Worth the detour",
  "body": "Excellent coffee and very consistent brunch menu.",
  "createdAtUtc": "2026-04-07T12:10:00Z",
  "updatedAtUtc": "2026-04-07T12:10:00Z",
  "author": {
    "id": "0cf0c39d-2de0-78cb-8d20-9435ba82ab5d",
    "handle": "hubert",
    "displayName": "Hubert",
    "avatarUrl": null
  },
  "place": {
    "id": "f69cf7f9-d7a6-7ca8-8a03-c940f4f49575",
    "name": "Cafe Example",
    "address": "Marszalkowska 1",
    "city": "Warsaw",
    "countryCode": "PL",
    "latitude": 52.2297,
    "longitude": 21.0122
  },
  "photos": []
}
```

### `GET /feed`

Response:

```json
{
  "items": [
    {
      "activityType": "placeReview",
      "itemId": "f39ad1eb-562d-7855-9246-b499e775f11e",
      "visibility": "friends",
      "createdAtUtc": "2026-04-07T12:10:00Z",
      "updatedAtUtc": "2026-04-07T12:10:00Z",
      "author": {
        "id": "0cf0c39d-2de0-78cb-8d20-9435ba82ab5d",
        "handle": "hubert",
        "displayName": "Hubert",
        "avatarUrl": null
      },
      "place": {
        "id": "f69cf7f9-d7a6-7ca8-8a03-c940f4f49575",
        "name": "Cafe Example",
        "address": "Marszalkowska 1",
        "city": "Warsaw",
        "countryCode": "PL",
        "latitude": 52.2297,
        "longitude": 21.0122
      },
      "rating": 5,
      "body": "Excellent coffee and very consistent brunch menu.",
      "title": "Worth the detour",
      "dishName": null,
      "photos": []
    }
  ],
  "nextCursorUtc": null
}
```

### `GET /map/places?north=...&south=...&east=...&west=...`

Response:

```json
[
  {
    "placeId": "f69cf7f9-d7a6-7ca8-8a03-c940f4f49575",
    "name": "Cafe Example",
    "address": "Marszalkowska 1",
    "city": "Warsaw",
    "countryCode": "PL",
    "latitude": 52.2297,
    "longitude": 21.0122,
    "visiblePlaceReviewCount": 2,
    "visibleDishReviewCount": 3,
    "averagePlaceRating": 4.5,
    "contributorCount": 2,
    "latestActivityAtUtc": "2026-04-07T12:10:00Z"
  }
]
```

## Visibility contract

- `private`
  - visible only to the owner
- `friends`
  - visible to the owner and accepted friends
- `public`
  - visible to any authenticated user

Enforcement rules:

- only the owner can edit or delete a review or photo
- `feed` and `map` are intentionally social-scope endpoints: they return owner + accepted-friend activity, not global public discovery
- public stranger content is still available through detail/list endpoints such as `/place-reviews`, `/dish-reviews`, `/places/{id}`, `/users/{id}`

## iOS screens to rewire later

- sign-in / session bootstrap
- my profile
- user profile
- incoming and outgoing friend requests
- friend list
- add place flow
- add place review flow
- add dish review flow
- place detail
- places list/search
- feed
- map
- photo upload/delete

## iOS code to remove later

- all CloudKit services
- all `CKShare`, `CKRecord`, `CKContainer` flows
- SwiftData business persistence models
- local merge logic between SwiftData and CloudKit
- local visibility filtering logic in repositories and view models
- account-change reload flows coupled to iCloud storage identity

## Required iOS implementation changes

- create a backend `AuthClient` for `POST /auth/apple`, `POST /auth/refresh`, `POST /auth/logout`
- store `accessToken` and `refreshToken` in Keychain
- send `identityToken` and `rawNonce` from Sign in with Apple to the backend
- move all repositories to backend-backed REST clients
- treat backend DTOs as source of truth
- use `/feed` and `/map/places` directly instead of reconstructing these screens from local data stores
