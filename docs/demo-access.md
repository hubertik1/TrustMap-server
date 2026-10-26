# Demo access for App Review

The iOS welcome screen has **Demo access** beside Privacy Policy and Support. It
opens a username/password sheet and signs in through `POST /auth/demo`. The account
uses the existing API, authorization, refresh tokens, Keychain storage, logout,
account deletion, moderation and privacy rules. Apple sign-in is unchanged.

## Configure

Demo access is disabled by default. No passwords or password hashes are committed.
Generate deployment settings once from the server repository:

```sh
python3 scripts/configure_demo_access.py
```

The ignored `.private/` folder contains owner-readable files:

- `app-review-credentials.txt`: username, random password and reviewer instructions.
- `demo-access.env`: environment variables to load for local development.
- `demo-access.azure.json`: the same settings in Azure CLI-compatible JSON.

For a local API, load the environment before starting the server:

```sh
source .private/demo-access.env
dotnet build src/TrustMap.Api '/bl:{}'
dotnet run --project src/TrustMap.Api --no-build --no-launch-profile
```

For production, deploy the updated server, then apply all five `DemoAccess__*`
settings through the hosting configuration. On Azure, use the existing app and
resource group with `az webapp config appsettings set --settings @.private/demo-access.azure.json`
(add `--name` and `--resource-group`). Avoid displaying the command's response,
which contains application settings. Do not publish `.private/` or put its files
in the iOS bundle. Keep the credentials stable throughout review.

The password uses PBKDF2-SHA256 with 600,000 iterations and a random 32-byte salt.
Only the configured demo account can authenticate through this endpoint. It uses
the existing authentication rate limiter and requires no Apple identity or MFA.

## Dataset

On the first successful demo sign-in, the API creates the fixture transactionally:

- Alex (reviewer); Mia, Eli and Nora (friends).
- Sam (incoming request), Luca (outgoing request), Zoe (not yet connected).
- Six fictional custom places near King's Cross, London; seven place reviews and
  six dish reviews, including reviews that the reviewer can edit/delete.
- A custom favorites category, notifications, two avatars and a dish photo.

Images reuse the app's existing fictional WelcomeDemo assets. They are embedded
in the server and copied through the normal photo storage pipeline. No external
image downloads or additional infrastructure are required.

The same default friend visibility protects demo reviews. Profiles are public and
searchable under the same rules as normal profiles; their names and bios identify
them as fictional demo profiles. This is not an isolated sandbox. The account has
ordinary user permissions and can use ordinary user actions.

Signing in again never recreates deleted reviews, restores friendships, removes
blocks or resets edits. If the demo account is deleted or suspended, sign-in is
denied. To provision a fresh dataset, generate settings into a new private output
directory and apply its new AccountId, salt and hash. This creates a separate new
fixture; it does not reactivate a deleted account. Old active fixtures must be
retired through normal account deletion when no longer needed.

## Before resubmitting

1. Deploy the API and apply settings before distributing the new iOS build.
2. Sign in from the actual build with the exact generated credentials.
3. Open Places without granting location. Open Sunday Table · Demo, report Eli's
   review, open his profile and block him. Verify the report reaches moderation.
4. Test own review editing/deletion, friend requests, logout, relaunch and refresh.
5. Test account deletion with a disposable fixture, then provision the final one.
6. Add credentials to App Review Information and the supplied steps to Notes. Include
   `https://trustmap.hubertik.com/terms/` and explain that Terms of Service is
   available on the welcome screen, in Demo access, and in Settings → About.

The published Terms of Service and community rules link to Apple’s standard EULA.
The app’s Local and Prod configurations include that URL. This does not introduce
a new acceptance screen or change the existing moderation UI; verify those flows
and actual report handling independently before claiming compliance.
