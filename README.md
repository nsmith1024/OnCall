# OnCall

OnCall is a shared .NET MAUI mobile app for clients and lawyers, backed by Firebase.

## Current local MVP

- Email/password registration and sign-in through the Firebase Auth emulator
- Secure mobile session storage and token refresh
- Server-controlled client/lawyer roles
- Authenticated profile creation
- Client legal-request creation
- Transactional lawyer acceptance so only one lawyer can claim a request
- Client request status and cancellation
- Lawyer presence, expiring offers, assignment restoration, and completion
- Durable notification outbox with production FCM/APNs delivery
- Five-minute, participant-only Jitsi JWT session credentials
- Closed-by-default Firestore and Storage rules

## Run locally

From `OnCallServer`:

```powershell
firebase emulators:start --only auth,functions,firestore --project oncall-564dc
```

Then run the Android target from Visual Studio. Android emulator traffic uses
`10.0.2.2` to reach the Firebase emulators on the Windows host. Debug builds
allow cleartext HTTP for this purpose; production will use HTTPS.

The Firebase Emulator UI is available at <http://127.0.0.1:4000>.

With the emulators running, execute the repeatable end-to-end backend test:

```powershell
cd OnCallServer\functions
npm run test:integration
```

## Build checks

```powershell
cd OnCallServer\functions
npm run build
npm test

cd ..\..\OnCallApp
dotnet build OnCall.Mobile\OnCall.Mobile.csproj -f net10.0-android
```

No Firebase production resources are required for the local flow.

## Swappable location providers

The app does not depend directly on a map vendor:

- `IDeviceCoordinateProvider` obtains latitude and longitude.
- `IReverseGeocoder` converts coordinates into jurisdiction data.
- `ILocationResolver` combines those operations for the UI.

The default `NativeReverseGeocoder` uses the native Android or iOS service
through MAUI. Replacing its dependency registration changes the provider
without changing pages or the legal-request workflow.

Firebase has a separate `ReverseGeocoder` provider contract so production can
verify the phone's result. It currently supports:

```text
GEOCODING_PROVIDER=client-native
GEOCODING_PROVIDER=nominatim
NOMINATIM_BASE_URL=https://geo.example.com
```

The prototype defaults to the client-native hint. The Nominatim adapter performs
server-side reverse geocoding and marks its jurisdiction as server-verified. A
future Google adapter can implement the same contract without changing matching,
Firestore documents, or mobile UI code.

## Production configuration (not yet enabled)

Production push delivery requires Android and iOS Firebase app registrations.
The resulting `google-services.json` and `GoogleService-Info.plist` files are
intentionally ignored by Git.

Before deploying Jitsi meeting credentials, configure:

- Firebase secret `JITSI_JWT_SECRET`, shared only with the self-hosted Jitsi server
- Function environment value `JITSI_SERVER_URL`, such as `https://meet.example.com`

Meeting credentials are issued only while a request is assigned, only to its
client and assigned lawyer, and expire after five minutes.
