import { describe, expect, it } from 'vitest';
import { MAX_PREVIEW_IMAGE_BYTES, buildPreview, shareIdFromPath } from './preview';

const page = new URL('https://instashare.mohitkumarverma.com/view/x');
const now = Date.parse('2026-10-20T12:00:00Z');
const cdn = 'https://cdn.test';

/** A fetch that serves one index.json, or a status code. */
const serve = (body: unknown, status = 200) =>
    (async (input: RequestInfo | URL) => {
        expect(String(input)).toMatch(/^https:\/\/cdn\.test\/.+\/index\.json$/);
        return new Response(status === 200 ? JSON.stringify(body) : 'not found', { status });
    }) as typeof fetch;

const file = (path: string, size = 1000, status = 'uploaded') => ({ path, size, status, url: `${cdn}/s/${path}` });

describe('shareIdFromPath', () => {
    it('takes the first segment after /view/', () => {
        expect(shareIdFromPath('/view/abc/Photos/x.jpg')).toBe('abc');
        expect(shareIdFromPath('/view/Q3%20report.pdf-123')).toBe('Q3 report.pdf-123');
        expect(shareIdFromPath('/other')).toBeNull();
        expect(shareIdFromPath('/view/%E0%A4')).toBeNull();
    });
});

describe('buildPreview', () => {
    it('uses the image itself for a single-image share', async () => {
        const p = await buildPreview('s', page, { CDN_BASE_URL: cdn },
            serve({ name: 'TerraceFloor.png', kind: 'file', expiresUtc: '2026-10-20T18:00:00Z', files: [file('TerraceFloor.png', 268_509)] }), now);
        expect(p.title).toBe('TerraceFloor.png');
        expect(p.image).toBe(`${cdn}/s/TerraceFloor.png`);
        expect(p.description).toBe('262.2 KB · expires in 6 h. Shared with InstaShare.');
        expect(p.imageSize).toBeUndefined();
    });

    it('describes a folder and uses its shallowest image', async () => {
        const p = await buildPreview('s', page, { CDN_BASE_URL: cdn }, serve({
            name: 'Trip', files: [file('Trip/notes.txt'), file('Trip/Deep/a.jpg'), file('Trip/cover.jpg'), file('Trip/v.mp4', 5, 'uploading')],
        }), now);
        expect(p.title).toBe('Trip');
        expect(p.image).toBe(`${cdn}/s/Trip/cover.jpg`);
        expect(p.description).toBe('4 files · 2.9 KB · still uploading. Shared with InstaShare.');
    });

    it('falls back to the InstaShare card for images that are too big, or not images', async () => {
        const big = await buildPreview('s', page, { CDN_BASE_URL: cdn }, serve({ files: [file('huge.jpg', MAX_PREVIEW_IMAGE_BYTES + 1)] }), now);
        expect(big.image).toBe('https://instashare.mohitkumarverma.com/og-image.png');
        expect(big.title).toBe('huge.jpg');

        const pdf = await buildPreview('s', page, { CDN_BASE_URL: cdn }, serve({ files: [file('report.pdf')] }), now);
        expect(pdf.image).toMatch(/og-image\.png$/);
        expect(pdf.imageSize).toEqual({ width: 1200, height: 630 });
    });

    it('prefers the preview the desktop app made, with its size', async () => {
        const p = await buildPreview('s', page, { CDN_BASE_URL: cdn }, serve({
            name: 'Trip', preview: { url: `${cdn}/s/.instashare/preview.jpg`, width: 1200, height: 630 },
            files: [file('Trip/huge.jpg', 9_000_000), file('Trip/clip.mp4', 50_000_000)],
        }), now);
        expect(p.image).toBe(`${cdn}/s/.instashare/preview.jpg`);
        expect(p.imageSize).toEqual({ width: 1200, height: 630 });
        expect(p.title).toBe('Trip');
    });

    it('reads old bare-array shares', async () => {
        const p = await buildPreview('s', page, { CDN_BASE_URL: cdn }, serve([file('Album/1.png'), file('Album/2.png')]), now);
        expect(p.title).toBe('Album');
        expect(p.image).toBe(`${cdn}/s/Album/1.png`);
    });

    it('says when a link has expired', async () => {
        const gone = await buildPreview('s', page, { CDN_BASE_URL: cdn }, serve(null, 404), now);
        expect(gone.title).toBe('This link has expired');

        const past = await buildPreview('s', page, { CDN_BASE_URL: cdn }, serve({ expiresUtc: '2026-10-20T11:00:00Z', files: [file('a.png')] }), now);
        expect(past.title).toBe('This link has expired');
    });

    it('keeps the generic card when the CDN has trouble', async () => {
        const p = await buildPreview('s', page, { CDN_BASE_URL: cdn }, serve(null, 500), now);
        expect(p.title).toBe('Files shared with you on InstaShare');
    });
});
