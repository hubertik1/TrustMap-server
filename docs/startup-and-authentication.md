# Startup and Apple sign-in on Azure Free F1

The backend supports Azure App Service Free F1 without Always On or periodic
keep-alive requests. Azure can still unload an idle application. No change inside
the API can eliminate the time Azure takes to start its process before forwarding
the first request.

## Backend behavior

- `AppleSignInWarmupService` starts one background fetch of Apple's OpenID
  configuration and signing keys when the process starts. It does not block HTTP
  startup, readiness checks, or TrustMap refresh-token requests.
- Warm-up and identity validation use the same singleton configuration manager.
  Successful warm-up lets subsequent sign-ins reuse its cached keys. The existing
  IdentityModel cache and refresh behavior remain in place.
- Each metadata HTTP request has a five-second timeout. The caller's wait for the
  combined OpenID configuration and key retrieval is limited to ten seconds.
  This limit covers Apple metadata, not Azure's process startup, database work,
  or the separate Apple authorization-code exchange (which has its own
  ten-second HTTP timeout when enabled).
- If warm-up fails, the API stays available and the next sign-in can fetch keys
  again. There is no background polling loop or attempt to keep F1 awake.
- A metadata/network failure during `POST /auth/apple` returns HTTP `503` with
  error code `apple_identity_service_unavailable`. It does not create an account
  or a TrustMap session.
- Invalid, expired, or incorrectly signed Apple identity tokens return HTTP
  `401` with error code `invalid_apple_identity_token`. Signature, issuer,
  audience, lifetime, and nonce checks still apply. Malformed tokens are rejected
  before waiting for metadata.
- Warm-up logs its elapsed time and, on failure, the exception type. Identity
  tokens, authorization codes, and Apple response bodies are not included in
  these logs or the new error responses.

## Companion app update

These server changes are compatible with older clients. The companion app now
implements the following behavior, which requires shipping an updated app:

1. Stored tokens survive timeouts, network failures, and temporary server
   errors. A definitive session rejection still clears the session.
2. Session restoration has a 45-second total grace period. A temporary connection
   failure does not show the retry screen early. The screen shows only localized
   generic error and retry copy, and retry uses the saved session.
3. The app checks the saved access-token expiry before requesting `/me`. If it
   expires within 30 seconds, it calls `/auth/refresh` directly and uses the profile returned in
   that response. This reduces normal expired-session restoration from three
   HTTP requests to one.
4. Safe startup profile reads can retry connection failures within the grace
   period. `/auth/apple` and `/auth/refresh` are not automatically replayed:
   Apple authorization codes are single-use and TrustMap refresh tokens rotate,
   so a lost response does not imply that the server did not process the request.
5. Concurrent refresh requests share one operation. Refreshed tokens are saved
   before callers retry their authenticated requests. Sign-out cancels startup
   and refresh work so late responses cannot restore the previous session.

Apple sign-in and refresh HTTP requests allow 45 seconds for a slow server.
The app changes do not enable Always On or change the Azure hosting plan.

On the welcome screen, the app sends one anonymous, uncached `GET /health/live`
per app launch. This lightweight endpoint skips database health checks; its plain
text response does not need JSON decoding. The probe overlaps with Apple's sign-in
sheet and never gates login or changes saved credentials, even if it fails.
Successful sign-in cancels a pending probe. This is not a periodic keep-alive:
session restoration and SwiftUI previews do not send a separate probe.

## Verification

`AuthStartupResilienceTests` covers non-blocking warm-up, refresh during pending
warm-up, shared signing-key caching, metadata outages and recovery, cancellation,
and rejection of invalid signature, issuer, audience, lifetime, nonce, and token
format. Tests use local fakes and generated RSA keys; they do not call Apple.
The app's `SessionRestorationTests` exercises the real client transport with a
fake URL protocol, including timeouts, deadlines, manual recovery, single-flight
refresh, definitive rejection, and preservation of credentials during outages.
