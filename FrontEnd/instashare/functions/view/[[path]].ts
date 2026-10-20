// Cloudflare Pages Function for /view/*.
// Chat apps (WhatsApp, Slack, Teams, ...) don't run JavaScript, so they only ever see the static
// link-preview tags in index.html. This fills them in per share, then serves the same page as before.

import { buildPreview, rewriteHead, shareIdFromPath, type PreviewEnv } from '../preview';

export const onRequestGet: PagesFunction<PreviewEnv> = async (context) => {
    const { request, env, next } = context;
    const page = await next(); // index.html, via the SPA fallback
    if (!(page.headers.get('content-type') ?? '').includes('text/html')) return page;

    const url = new URL(request.url);
    const shareId = shareIdFromPath(url.pathname);
    if (!shareId) return page;

    try {
        const preview = await buildPreview(shareId, url, env, fetch);
        return rewriteHead(page, preview);
    } catch (error) {
        // Never break the page over a preview.
        console.error('preview failed', error);
        return page;
    }
};
