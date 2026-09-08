# OnCall

OnCall is a shared .NET MAUI mobile app for clients and lawyers, backed by Firebase.

## Current local MVP

- Email/password registration and sign-in through the Firebase Auth emulator
- Secure mobile session storage and token refresh
- Server-controlled client/lawyer roles
- Authenticated profile creation
- Client legal-request creation
- Transactional lawyer acceptance so only one lawyer can claim a request
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

cd ..\..\OnCallApp
dotnet build OnCall.Mobile\OnCall.Mobile.csproj -f net10.0-android
```

No Firebase production resources are required for the local flow.
