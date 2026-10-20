// Link-preview tags for one share. Kept free of Pages APIs except rewriteHead, so it can be unit-tested.

import type { FileEntry } from '../src/models/fileEntry';
import { formatBytes, formatDuration, getFileKind, normalizePath, parseIndex } from '../src/utils/utils';

export interface PreviewEnv {
    /** Optional override; defaults to the production CDN. */
    CDN_BASE_URL?: string;
}

export interface Preview {
    title: string;
    description: string;
    image: string;
    /** Size hints help WhatsApp lay out the card before the image loads. */
    imageSize?: { width: number; height: number };
    imageAlt: string;
    card: 'summary' | 'summary_large_image';
}

const DEFAULT_CDN = 'https://cdn.instashare.mohitkumarverma.com';
// WhatsApp quietly skips images much over this, so bigger ones fall back to the InstaShare card.
// The limit isn't documented; 600 KB is a conservative guess that keeps typical screenshots and PNGs.
export const MAX_PREVIEW_IMAGE_BYTES = 600 * 1024;
// Formats chat apps reliably render as preview images.
const PREVIEW_IMAGE_EXTS = new Set(['jpg', 'jpeg', 'png', 'gif', 'webp']);

/** "/view/<shareId>/..." → "<shareId>", decoded. */
export function shareIdFromPath(pathname: string): string | null {
    const match = /^\/view\/([^/]+)/.exec(pathname);
    if (!match) return null;
    try {
        return decodeURIComponent(match[1]);
    } catch {
        return null;
    }
}

export async function buildPreview(shareId: string, pageUrl: URL, env: PreviewEnv, fetcher: typeof fetch, now = Date.now()): Promise<Preview> {
    const cdn = env.CDN_BASE_URL ?? DEFAULT_CDN;
    const fallback = fallbackPreview(pageUrl);

    const res = await fetcher(`${cdn}/${encodeURIComponent(shareId)}/index.json`, {
        // Previews are fetched once per chat app; a short cache spares the CDN when a link is posted in several chats.
        cf: { cacheTtl: 60, cacheEverything: true },
    } as RequestInit);
    if (res.status === 404 || res.status === 403)
        return { ...fallback, title: 'This link has expired', description: 'Shared files are deleted automatically after a while.' };
    if (!res.ok) return fallback;

    const index = parseIndex(await res.json());
    const files = index.files;
    if (files.length === 0) return fallback;

    const expires = index.expiresUtc ? Date.parse(index.expiresUtc) - now : NaN;
    if (Number.isFinite(expires) && expires <= 0)
        return { ...fallback, title: 'This link has expired', description: 'Shared files are deleted automatically after a while.' };

    const isSingleFile = files.length === 1 && !normalizePath(files[0].path).includes('/');
    const name = index.name ?? (isSingleFile ? normalizePath(files[0].path) : normalizePath(files[0].path).split('/')[0]);
    const totalSize = files.reduce((sum, f) => sum + (f.size ?? 0), 0);

    const parts = [isSingleFile ? formatBytes(totalSize) : `${files.length} ${files.length === 1 ? 'file' : 'files'} · ${formatBytes(totalSize)}`];
    if (files.some((f) => f.status === 'uploading')) parts.push('still uploading');
    if (Number.isFinite(expires)) parts.push(`expires in ${formatDuration(expires)}`);
    const description = `${capitalize(parts.join(' · '))}. Shared with InstaShare.`;

    // Made by desktop 2.1+: the image, a video frame, a PDF page or a collage, already small.
    if (index.preview?.url) {
        const { url, width, height } = index.preview;
        return {
            title: name, description, image: url, imageAlt: name, card: 'summary_large_image',
            imageSize: width && height ? { width, height } : undefined,
        };
    }

    // Older shares: use an uploaded image if one is small enough.
    const image = pickImage(files);
    return image
        ? { title: name, description, image: image.url, imageAlt: name, card: 'summary_large_image' }
        : { ...fallback, title: name, description, imageAlt: name };
}

/** The share's own picture when there's a suitable one: the single image, or the first image in a folder. */
function pickImage(files: FileEntry[]): FileEntry | undefined {
    return files
        .filter((f) => f.status === 'uploaded' && (f.size ?? Infinity) <= MAX_PREVIEW_IMAGE_BYTES)
        .filter((f) => getFileKind(f.path) === 'image' && PREVIEW_IMAGE_EXTS.has(f.path.split('.').pop()!.toLowerCase()))
        .sort((a, b) => normalizePath(a.path).split('/').length - normalizePath(b.path).split('/').length)[0];
}

function fallbackPreview(pageUrl: URL): Preview {
    return {
        title: 'Files shared with you on InstaShare',
        description: 'Open to preview or download. This link expires on its own.',
        image: `${pageUrl.origin}/og-image.png`,
        imageSize: { width: 1200, height: 630 },
        imageAlt: 'InstaShare',
        card: 'summary_large_image',
    };
}

const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

/** Swaps the static tags in index.html for this share's. Everything else in the page is untouched. */
export function rewriteHead(page: Response, preview: Preview): Response {
    const set = (selector: string, value: string) => ({
        element: (el: Element) => { el.setAttribute('content', value); },
        selector,
    });
    const handlers = [
        set('meta[property="og:title"]', preview.title),
        set('meta[property="og:description"]', preview.description),
        set('meta[name="description"]', preview.description),
        set('meta[property="og:image"]', preview.image),
        set('meta[property="og:image:alt"]', preview.imageAlt),
        set('meta[name="twitter:card"]', preview.card),
    ];

    let rewriter = new HTMLRewriter()
        .on('title', { element: (el) => { el.setInnerContent(`${preview.title} · InstaShare`); } });
    for (const h of handlers) rewriter = rewriter.on(h.selector, { element: h.element });
    // index.html carries the fallback image's size; replace it, or drop it when the size isn't known.
    const size = preview.imageSize;
    rewriter = size
        ? rewriter
            .on('meta[property="og:image:width"]', { element: (el) => { el.setAttribute('content', String(size.width)); } })
            .on('meta[property="og:image:height"]', { element: (el) => { el.setAttribute('content', String(size.height)); } })
        : rewriter.on('meta[property="og:image:width"], meta[property="og:image:height"]', { element: (el) => { el.remove(); } });

    const response = rewriter.transform(page);
    const headers = new Headers(response.headers);
    // The HTML now differs per share and per moment (expiry); don't let it be cached as one page.
    headers.set('cache-control', 'public, max-age=0, must-revalidate');
    return new Response(response.body, { status: response.status, headers });
}
