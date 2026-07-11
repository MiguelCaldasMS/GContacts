# GContacts

A small .NET 10 console app that finds the contacts in your Google account that have
**neither a phone number nor an email**, prints **all available metadata** (name, addresses,
organizations, and every other field the People API returns) for each, and then **permanently
deletes** them (after a confirmation prompt) using the
[Google People API](https://developers.google.com/people).

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

- **Deletion is permanent.** The app prints each matching contact and asks you to type `yes` before
  deleting; there is no undo. Review the output before confirming.
- If Google returns a **quota/rate-limit** error, the app waits 5 minutes and retries that one
  contact once; a second quota error in a row (or any other error) stops the run.
- The scope requested is `contacts` (full read/write). The app uses it to **delete** the matching
  contacts; create and update operations aren't implemented.
- If you already authorized once with a narrower scope, delete the cached token folder
  `%AppData%\GContacts\token` so the consent screen reappears and includes the write permission.
- The Google People API does **not** expose a contact creation/"added" timestamp, so no "added"
  date is shown. Only the last-modified time (`updateTime`) is available.
- `client_secret.json` and the cached token contain secrets and are excluded via `.gitignore`.
