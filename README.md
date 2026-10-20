# InstaShare

Share files and folders from Windows with a link. Links expire on their own.

| Folder | What it is | Hosted on |
| --- | --- | --- |
| `InstaShare/` | Windows desktop app (WPF, .NET 10) | Users' PCs |
| `Api/` | API Worker. Holds the R2 keys and signs uploads | Cloudflare Workers, `api.instashare.mohitkumarverma.com` |
| `FrontEnd/instashare/` | Viewer for share links | Cloudflare Pages, `instashare.mohitkumarverma.com` |

Files are stored in the `instashare` R2 bucket and served from `cdn.instashare.mohitkumarverma.com`.

## How an upload works

1. The app asks the API to create a share. The API records the share's expiry and returns a token for it.
2. The app asks the API for upload links. Each link is signed for one file and its exact size.
3. The app uploads the file straight to R2. The bytes never pass through the Worker.
4. Every hour, the Worker deletes shares that have expired.

The exe contains no keys. The worst someone can do with it is upload within the size limit, and those files are deleted when they expire.

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
   npm run deploy
   ```

   Deploying also sets up `api.instashare.mohitkumarverma.com` as a custom domain.

3. Once the new app is out, **delete the old R2 API token** that was embedded in earlier builds.

Tests: `npm test`.

## Releasing the desktop app

1. Increase `<Version>` in `InstaShare/InstaShare.csproj`. The installer compares versions to decide between installing and updating.
2. Publish:

   ```sh
   cd InstaShare
   dotnet publish -c Release -p:PublishProfile=SingleExe
   ```

3. Share `InstaShare/bin/publish/InstaShare.exe`.

Running the exe opens an installer that:

- Copies the exe to `%LocalAppData%\Programs\InstaShare`.
- Adds a Start menu entry, and optionally a desktop shortcut.
- Adds **Share with InstaShare** to the right-click menu. On Windows 11 it's under *Show more options*.
- Adds InstaShare to **Send to**, and optionally starts it with Windows.
- Registers it in *Apps & features* so it can be uninstalled.

It installs for the current user only, so it never asks for admin rights. Running a newer exe updates the installed copy and keeps the user's settings.

The exe isn't code-signed, so Windows SmartScreen shows "Windows protected your PC" the first time. Click **More info → Run anyway**. Signing it with a code-signing certificate removes the warning.

To try the installer from a Debug build, run it with `--install`.

Tests: `dotnet test` from the repo root.
