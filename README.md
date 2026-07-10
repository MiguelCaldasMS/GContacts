# GContacts

A small .NET 10 console app that lists the contacts in your Google account using the
[Google People API](https://developers.google.com/people) and prints the **name**, **email**,
**added date**, and **modified date** of the first 100.

## One-time setup

1. Go to the [Google Cloud Console](https://console.cloud.google.com/) and create (or select) a project.
2. Enable the **People API** for that project (APIs & Services → Library → "People API" → Enable).
3. Configure the **OAuth consent screen** (External is fine for personal use) and add your Google
   account as a test user.
4. Create credentials: **APIs & Services → Credentials → Create credentials → OAuth client ID**,
   application type **Desktop app**.
5. Download the JSON and save it in this folder as **`client_secret.json`**.

## Run

```powershell
dotnet run
```

The first run opens a browser so you can grant access to **read and manage** your contacts. The token is
cached under `%AppData%\GContacts\token`, so you won't be prompted again on later runs.

## Notes

- The scope requested is `contacts` (full read/write) so the app can also create, update, and delete
  contacts. Those write operations aren't implemented yet — only reading is wired up so far.
- If you already authorized once with a narrower scope, delete the cached token folder
  `%AppData%\GContacts\token` so the consent screen reappears and includes the write permission.
- The Google People API does **not** expose a contact creation/"added" timestamp, so that column
  shows `N/A`. Only the last-modified time (`updateTime`) is available.
- `client_secret.json` and the cached token contain secrets and are excluded via `.gitignore`.
