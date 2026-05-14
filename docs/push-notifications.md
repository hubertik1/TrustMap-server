# Push Notifications

TrustMap sends APNs pushes from the backend after the in-app notification record is saved. Push delivery is best-effort: review and friend-request creation must not depend on APNs availability.

## Apple Developer Setup

1. Enable Push Notifications for the TrustMap App ID / Bundle ID.
2. Create an APNs Auth Key in Apple Developer.
3. Record the Team ID and Key ID.
4. Download the `.p8` private key once.
5. Do not commit the `.p8` file or its contents to git.

The backend uses token-based APNs authentication with the configured Team ID, Key ID, Bundle ID, and private key.

## Backend Configuration

Local development and automated tests should keep the no-op provider:

```text
PushNotifications__Provider=noop
```

Production must configure APNs through environment variables or a secret store:

```text
PushNotifications__Provider=apns
PushNotifications__Apns__Enabled=true
PushNotifications__Apns__TeamId=...
PushNotifications__Apns__KeyId=...
PushNotifications__Apns__BundleId=com.hubertik.TrustMap
PushNotifications__Apns__PrivateKeyBase64=...
PushNotifications__Apns__RequestTimeoutSeconds=10
```

`PrivateKeyBase64` should be the base64 encoding of the full `.p8` PEM text. It is preferred for Azure App Service because it fits in a single-line app setting. The backend also supports `PrivateKeyPem` and `PrivateKeyPath`.

Do not place real APNs secrets in `appsettings.json`.

## Azure App Service

Add the APNs settings as App Service application settings. Mark APNs secrets as deployment slot settings so staging and production slots keep separate credentials during swaps.

Restart the App Service after changing these settings.

## APNs Environments

The iOS app sends each device token with an environment:

- Local debug builds use `development`, which sends to `https://api.sandbox.push.apple.com`.
- TestFlight and App Store builds use `production`, which sends to `https://api.push.apple.com`.

The backend chooses the APNs endpoint from the stored device token environment, so development tokens are never sent to production APNs and production tokens are never sent to the sandbox endpoint.
