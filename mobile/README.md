# Helpdesk mobile (Flutter)

The requester's side of the helpdesk on a phone or in a browser: sign in, see your tickets, open one (the category is
suggested while you type), comment, and confirm, reject, reopen or cancel. It calls the same API as the web UI and shows
exactly the buttons the server lists in `actions`, so the rules live in one place.

```bash
cd mobile
flutter pub get
flutter test                                             # 11 tests: API client and the sign-in / confirm flow
flutter run -d chrome --dart-define=API_URL=http://localhost:8080
```

The API must be running (`docker compose up --build` in the repository root). To serve a release build the way the demo does:

```bash
flutter build web --release
cd build/web && python -m http.server 5001               # http://localhost:5001
```

`docker-compose.yml` already allows that origin (`App__CorsOrigins`). For an Android emulator use
`--dart-define=API_URL=http://10.0.2.2:8080` (the emulator's name for your computer).

## What it does not do

- **No live updates.** The web UI gets SignalR notifications; here a list refreshes when you pull it down or come back to it.
- **The token is kept in memory only**, so closing the app signs you out. Saving it needs secure storage on the device
  (`flutter_secure_storage`), which is not added yet.
- **Requesters only.** There is no agent queue in the app.
- **Verified on the web build and in widget tests.** The Android project is generated but has not been built or run on a device.
