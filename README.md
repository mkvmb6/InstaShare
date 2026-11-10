# InstaShare

Share files and folders from Windows with a link. Links expire on their own.

| Folder | What it is | Hosted on |
| --- | --- | --- |
| `InstaShare/` | Windows desktop app (WPF, .NET 10) | Users' PCs |
| `Api/` | API Worker. Holds the R2 keys, signs uploads and deletes expired shares. | Cloudflare Workers, `api.instashare.mohitkumarverma.com` |
| `FrontEnd/instashare/` | Viewer for share links, plus the Pages Function that makes link previews. See its [README](FrontEnd/instashare/README.md). | Cloudflare Pages, `instashare.mohitkumarverma.com` |

Files are stored in the `instashare` R2 bucket and served from `cdn.instashare.mohitkumarverma.com`.

## How an upload works

1. The app asks the API to create a share. The API records the share's expiry and returns a token for it. Only that token can upload into the share or delete it.
2. The app asks the API for upload links. Each link is signed for one file and its exact size.
3. The app uploads the file straight to R2. The bytes never pass through the Worker.
4. While the files upload, the app writes the share's `index.json`, which the viewer reads. It also makes the preview picture chat apps show for the link.
5. Every hour, the Worker deletes shares that have expired.

Neither the exe nor the repo contains any keys. Creating a share or getting upload links needs an API key you've issued (see [API keys](#api-keys)), and each key has a daily upload cap. Deleting a share needs only that share's token, so people can always remove what they shared.

### What gets shared

- **One file:** a link to that file. The viewer shows it large, with a preview and a Download button.
- **A folder:** a link to the folder, keeping its subfolders.
- **Several items at once** (an Explorer selection, Send to, a drop, or a multi-file Browse): one link for all of them.
  - Files sit at the top of the share, and folders keep their structure.
  - Two files with the same name become `name (2)`.
  - The share is named after the folder the items came from.
  - Explorer starts one copy of the app per selected item. The running app gathers them, waiting for 1 s without a new item, up to 15 s.
  - The right-click entry works for up to 100 items, Windows' limit for this kind of entry.

### Limits

| Limit | Value | Where it's set |
| --- | --- | --- |
| Expiry choices | 1 h, 6 h, 12 h, 1 day, 2 days, 7 days | `InstaShare/ViewModels/ExpiryOption.cs` and `ALLOWED_EXPIRY_HOURS` in `Api/src/shares.ts`. Keep the two the same, because the API rejects any other value. |
| Largest file | 10 GB | `MAX_OBJECT_BYTES` in `Api/wrangler.jsonc` |
| Cleanup | Every hour, at minute 17 | `triggers.crons` in `Api/wrangler.jsonc` |

> **Bucket lifecycle rule:** the `instashare` bucket has a rule (`DeleteAfter1Day`) that deletes every object after 1 day. Until it's raised to 8 days or more, 2-day and 7-day shares stop working after the first day. Check it with `npx wrangler r2 bucket lifecycle list instashare`.

### `index.json`

Each share has an `index.json` at its top level. The desktop app writes it and keeps it updated during the upload. The viewer and the Pages Function read it.

```json
{
  "name": "Trip",
  "kind": "folder",
  "expiresUtc": "2026-10-21T18:00:00Z",
  "preview": { "url": "https://cdn…/<share>/.instashare/preview.jpg", "width": 1200, "height": 630 },
  "files": [
    { "path": "Trip/beach.jpg", "url": "https://cdn…/<share>/Trip/beach.jpg", "size": 268509, "status": "uploaded" }
  ]
}
```

- `status` is `uploading`, `uploaded` or `failed`.
- Each file's `url` is percent-encoded segment by segment, so names with spaces, `#`, `?` or `%` work. `path` holds the plain name.
- `preview` appears once the preview picture is uploaded. The desktop app makes it from the local files:
  - one image becomes a smaller copy of it
  - a video becomes a frame with a play button
  - a PDF becomes its first page
  - several items become a collage of up to four, with "+N" for the rest
- Shares from versions before 2.0 used a plain array of files. The viewer still reads that format.

## Deploying the API

One-time setup:

1. In the Cloudflare dashboard, go to R2 → Manage API tokens. Create a token with **Object Read & Write**, limited to the `instashare` bucket.
2. Set up and deploy the Worker:

   ```sh
   cd Api
   npm install
   npx wrangler login
   npx wrangler secret put R2_ACCOUNT_ID
   npx wrangler secret put R2_ACCESS_KEY_ID
   npx wrangler secret put R2_SECRET_ACCESS_KEY
   npx wrangler secret put TOKEN_SECRET    # any long random string; changing it invalidates existing share tokens
   npx wrangler kv namespace create API_KEYS   # put the id it prints into kv_namespaces in wrangler.jsonc
   npm run deploy
   npm run keys -- create "Your name"          # paste the key into InstaShare → Settings → API key
   ```

   Deploying also sets up `api.instashare.mohitkumarverma.com` as a custom domain.

3. **Delete the old R2 API token** that was embedded in builds before the API existed. Anyone with one of those exes can use it until it's deleted.

Later deploys: `npm run deploy` from `Api/`. Tests: `npm test`.

When you upgrade `wrangler`, also raise `@cloudflare/workers-types` to the version it lists, or `npm install` fails:

```sh
npm view wrangler@<version> peerDependencies
```

## API keys

Each person who uploads needs their own key. Run these from `Api/`:

```sh
npm run keys -- create "Name"           # new key, 50 GB a day; shown once
npm run keys -- create "Name" --gb 10   # new key with a different daily cap
npm run keys -- list                    # who has a key, and their caps
npm run keys -- revoke "Name"           # stops that key; others keep working
```

- **Storage:** KV (`API_KEYS`) stores only a hash of each key, with its name and cap. A lost key can't be recovered; revoke it and create a new one.
- **Daily cap:** each key has an upload cap per UTC day, counted in a Durable Object (`QuotaCounter`). Upload links are signed for an exact size, so the count can't be cheated. A request that would go over the cap is refused, and nothing from it is counted.
- **Requirement switch:** `REQUIRE_API_KEY` in `wrangler.jsonc` is `"true"`, so requests without a key are refused. While updating apps you can deploy with `--var REQUIRE_API_KEY:false` to let them through for a short time. A key that is sent still has to be valid.
- **Free plan:** KV and SQLite-backed Durable Objects are both included in the Workers Free plan.

## Deploying the viewer

```sh
cd FrontEnd/instashare
npm run build
npx wrangler pages deploy dist --branch main
```

Use `--branch main` so the deploy goes to production, and deploy with wrangler so `functions/` goes up too. The [viewer README](FrontEnd/instashare/README.md) explains both.

Tests: `npm test`.

When `index.json` changes format, deploy the viewer before releasing the desktop app. Otherwise the live viewer receives shares it can't read.

## Releasing the desktop app

1. Increase `<Version>` in `InstaShare/InstaShare.csproj`. The installer compares versions to decide between installing and updating.
2. Publish:

   ```sh
   cd InstaShare
   dotnet publish -c Release -p:PublishProfile=SingleExe
   ```

3. Share `InstaShare/bin/publish/InstaShare.exe`, a single self-contained file of about 80 MB.

Running the exe opens an installer that:

- Copies the exe to `%LocalAppData%\Programs\InstaShare`.
- Adds a Start menu entry, and optionally a desktop shortcut.
- Adds **Share with InstaShare** to the right-click menu. On Windows 11 it's under *Show more options*.
- Adds InstaShare to **Send to**, and optionally starts it with Windows.
- Registers it in *Apps & features* so it can be uninstalled.

It installs for the current user only, so it never asks for admin rights. Running a newer exe updates the installed copy and keeps the user's settings. Settings and share history are stored in `%LocalAppData%\InstaShare`.

The exe isn't code-signed, so Windows SmartScreen shows "Windows protected your PC" the first time. Click **More info → Run anyway**. Signing it with a code-signing certificate removes the warning.

To try the installer from a Debug build, run it with `--install`.

Tests: `dotnet test` from the repo root.

## Tests

| Part | Command | Covers |
| --- | --- | --- |
| Desktop app | `dotnet test` (repo root) | Upload progress, share history, links and encoding, the `index.json` format, preview pictures, multi-select grouping |
| API Worker | `npm test` in `Api/` | Request signing (against AWS's published examples), share tokens, API keys and daily caps, input checks, cleanup |
| Viewer | `npm test` in `FrontEnd/instashare/` | Folder listing, formatting, link-preview tags |
