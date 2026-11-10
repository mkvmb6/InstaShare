# InstaShare viewer

The web page that share links open: `instashare.mohitkumarverma.com/view/<share>`. It's a React app hosted on Cloudflare Pages, plus one Pages Function. See the [main README](../../README.md) for how it fits with the desktop app and the API.

## What's here

| Path | What it is |
| --- | --- |
| `src/` | The viewer. It reads the share's `index.json` from the CDN and shows the files, with previews, downloads, search, and a share menu with copy link and QR code. |
| `functions/view/[[path]].ts` | Pages Function for `/view/*`. It fills in the link-preview tags for each share, because chat apps don't run JavaScript. |
| `functions/preview.ts` | The tag logic. It reads `index.json` and uses the share's name, size, expiry and preview picture. |
| `public/` | Favicon, and the generic link-preview image `og-image.png` |

## Develop

```sh
npm install
npm run dev
```

The viewer reads shares from the production CDN. To point it somewhere else, set `VITE_CDN_BASE_URL` before building or starting dev, e.g. `VITE_CDN_BASE_URL=http://localhost:4174/cdn`.

`npm run dev` doesn't run the Pages Function. To try the link-preview tags locally:

```sh
npm run build
npx wrangler pages dev dist
curl -s -A WhatsApp http://127.0.0.1:8788/view/<share-id> | grep og:
```

## Test

```sh
npm test        # Unit tests for the viewer's helpers and the Pages Function
npm run lint
npx tsc -p functions
```

## Deploy

```sh
npm run build
npx wrangler pages deploy dist --branch main
```

- **Use `--branch main`.** Without it, wrangler names the upload after your current git branch. Any branch other than `main` becomes a Preview deployment, and the live site doesn't change.
- **Deploy with wrangler, not the dashboard.** It uploads `functions/` along with `dist`. Dragging `dist` into the dashboard leaves the Function out, and every link then gets the generic preview.
