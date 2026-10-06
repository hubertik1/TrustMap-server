# Account deletion: Apple revocation and durable media cleanup

## Apple configuration

Create a key with **Sign in with Apple** enabled in Apple Developer > Certificates, Identifiers & Profiles > Keys. Configure its primary App ID for TrustMap. An APNs-only key cannot be used for this flow.

Set these hosting settings/secrets:

```text
AppleAuth__TokenManagementEnabled=true
AppleAuth__TeamId=<Apple Developer team ID>
AppleAuth__KeyId=<Sign in with Apple key ID>
AppleAuth__AllowedAudiences__0=<actual app bundle ID>
AppleAuth__PrivateKeyPem=<complete secret PEM content of the downloaded .p8 file>
```

Alternatively set `AppleAuth__PrivateKeyPath` to a private server file, leaving `PrivateKeyPem` empty. Relative paths resolve from the API content root. Exactly one key source must be configured. Never commit the key. Enabled token management validates credentials at startup, including the P-256 private key. Each verified identity token's audience selects the client ID; it must be in `AllowedAudiences`.

Without a configured Apple key, `TokenManagementEnabled` stays false and startup emits a warning. Apple identity verification and account deletion still work, but **automatic Apple authorization revocation is not available for tokenless accounts**. Enable and configure this before release.

Preserve the existing Data Protection key directory, configured by `Moderation__DataProtectionKeyDirectory`, on durable private storage. The same key ring now protects Apple refresh tokens under a distinct purpose. Do not delete or replace it during deployments. On Azure use a persistent path (for example under `/home`); keep the SQLite database on persistent storage too. Docker Compose already persists `/app/App_Data` including the database and default key directory. Restrict access to both the key directory and database.

## Login

When token management is enabled, `/auth/apple` requires the fresh `authorizationCode` already sent by the app. The server exchanges it immediately with Apple's `/auth/token`. A short-lived ES256 client secret is generated from the key. The returned identity token is verified and matched to the original subject, audience and nonce. Only the encrypted refresh token and its client ID are stored on `AppleIdentity`; codes, client secrets and token bodies are never logged. A failed exchange does not create a local account/session. The app must obtain a fresh code when retrying a failed login.

Existing users gain a stored token at their next Apple sign-in. Users deleting before that can still delete their account; the updated app shows Apple's manual revocation steps afterwards. Missing credentials never prevent account deletion.

## Deletion and retries

`DELETE /me` retains its existing `204` response for older clients. `DELETE /me?includeResult=true` returns `200`:

```json
{"requiresManualAppleRevocation":false,"cleanupPending":true}
```

The app clears its session after either successful response. A legacy `204` is treated conservatively as requiring manual Apple revocation. The app displays pending-cleanup information and, when needed, the Settings > Apple Account > Sign in with Apple > TrustMap > Delete steps.

The account-deletion transaction saves independent `account_deletion_jobs` before committing account/session/identity/photo record deletion:

- one media job for every original, medium and thumbnail storage key, including the avatar;
- an Apple revocation job carrying the existing encrypted refresh token and client ID, before the identity row is removed.

After commit, the request attempts up to 50 jobs within 15 seconds. Remaining work stays in SQLite. A worker checks every 30 seconds, claims batches with a two-minute lease, and retries failures with exponential delay capped at six hours. Jobs have no retry-count expiry and are removed only after a successful external operation. Process shutdown/crash leaves a durable lease that becomes eligible again. The worker supports competing instances with conditional database claims; storage deletion and Apple's repeated revocation are idempotent. Network calls never run inside the account-deletion transaction. Exceptions are logged by type with the job ID, without file paths, user IDs or tokens.

Automatic revocation sends the stored token to Apple's `/auth/revoke`. Apple returns `200` for both newly revoked and previously revoked tokens. Key-ring or Apple configuration failures keep the job queued for later repair/retry.

This change does not alter moderation-evidence cleanup, log/backup retention or the existing deleted-user record policy.

References: [Apple account deletion and token revocation](https://developer.apple.com/documentation/technotes/tn3194-handling-account-deletions-and-revoking-tokens-for-sign-in-with-apple), [Apple token validation](https://developer.apple.com/documentation/signinwithapplerestapi/generate-and-validate-tokens), [Apple token revocation](https://developer.apple.com/documentation/signinwithapplerestapi/revoke-tokens), [manual revocation](https://support.apple.com/en-us/102571).
