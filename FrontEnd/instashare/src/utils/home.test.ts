import { describe, expect, it } from 'vitest';
import { MAX_RECENT, clearRecent, forgetShare, loadRecent, rememberShare } from './recent';
import { shareLinkTarget } from './utils';

const host = 'instashare.mohitkumarverma.com';
const uuid = '1c4f174a-ae51-48ac-bc9a-efc21a152a92';

describe('shareLinkTarget', () => {
    it('takes a whole link, keeping its encoding', () => {
        expect(shareLinkTarget(`https://${host}/view/TerraceFloor.png-${uuid}`, host)).toBe(`/view/TerraceFloor.png-${uuid}`);
        expect(shareLinkTarget(`https://${host}/view/${uuid}/My%20Trip/Day%201/`, host)).toBe(`/view/${uuid}/My%20Trip/Day%201`);
        expect(shareLinkTarget(`  ${host}/view/${uuid}/Trip  `, host)).toBe(`/view/${uuid}/Trip`);
    });

    it('accepts preview deployments and the site it is on', () => {
        expect(shareLinkTarget(`https://abc123.instashare.pages.dev/view/${uuid}/T`, host)).toBe(`/view/${uuid}/T`);
        expect(shareLinkTarget(`http://localhost:4173/view/${uuid}/T`, 'localhost')).toBe(`/view/${uuid}/T`);
    });

    it('takes just the part after /view/, typed plain or encoded', () => {
        expect(shareLinkTarget(`${uuid}/My Trip`, host)).toBe(`/view/${uuid}/My%20Trip`);
        expect(shareLinkTarget(`${uuid}/My%20Trip`, host)).toBe(`/view/${uuid}/My%20Trip`);
        expect(shareLinkTarget(`/view/${uuid}/a#1`, host)).toBe(`/view/${uuid}/a%231`);
        expect(shareLinkTarget(`TerraceFloor.png-${uuid}`, host)).toBe(`/view/TerraceFloor.png-${uuid}`);
    });

    it('rejects links that are not InstaShare share links', () => {
        expect(shareLinkTarget('', host)).toBeNull();
        expect(shareLinkTarget('   ', host)).toBeNull();
        expect(shareLinkTarget(`https://example.com/view/${uuid}`, host)).toBeNull();
        expect(shareLinkTarget(`https://cdn.${host}/${uuid}/Trip/a.jpg`, host)).toBeNull();
        expect(shareLinkTarget(`https://${host}/`, host)).toBeNull();
        expect(shareLinkTarget(`https://${host}/view/`, host)).toBeNull();
        expect(shareLinkTarget('https://', host)).toBeNull();
    });
});

/** A Storage stand-in; Node has no localStorage. */
const memoryStorage = () => {
    const data = new Map<string, string>();
    return {
        getItem: (k: string) => data.get(k) ?? null,
        setItem: (k: string, v: string) => void data.set(k, v),
        removeItem: (k: string) => void data.delete(k),
        data,
    };
};

describe('recent shares', () => {
    const now = Date.parse('2026-11-10T12:00:00Z');
    const later = (h: number) => new Date(now + h * 3600_000).toISOString();

    it('keeps the newest first, without duplicates', () => {
        const s = memoryStorage();
        rememberShare({ id: 'a', name: 'A', kind: 'folder', expiresUtc: later(5) }, now, s);
        rememberShare({ id: 'b', name: 'B', kind: 'image', expiresUtc: later(5) }, now + 1000, s);
        rememberShare({ id: 'a', name: 'A again', kind: 'folder', expiresUtc: later(5) }, now + 2000, s);

        expect(loadRecent(now + 3000, s).map((r) => r.name)).toEqual(['A again', 'B']);
    });

    it('keeps at most a handful', () => {
        const s = memoryStorage();
        for (let i = 0; i < MAX_RECENT + 3; i++) rememberShare({ id: `s${i}`, name: `S${i}`, kind: 'other' }, now + i, s);
        const list = loadRecent(now + 100, s);
        expect(list).toHaveLength(MAX_RECENT);
        expect(list[0].id).toBe(`s${MAX_RECENT + 2}`);
    });

    it('leaves out shares that have expired', () => {
        const s = memoryStorage();
        rememberShare({ id: 'old', name: 'Old', kind: 'folder', expiresUtc: later(1) }, now, s);
        rememberShare({ id: 'new', name: 'New', kind: 'folder', expiresUtc: later(48) }, now, s);
        expect(loadRecent(now + 2 * 3600_000, s).map((r) => r.id)).toEqual(['new']);
    });

    it('forgets one, or all', () => {
        const s = memoryStorage();
        rememberShare({ id: 'a', name: 'A', kind: 'folder' }, now, s);
        rememberShare({ id: 'b', name: 'B', kind: 'folder' }, now, s);
        forgetShare('a', now, s);
        expect(loadRecent(now, s).map((r) => r.id)).toEqual(['b']);
        clearRecent(s);
        expect(loadRecent(now, s)).toEqual([]);
        expect(s.data.size).toBe(0);
    });

    it('survives damaged or missing storage', () => {
        const s = memoryStorage();
        s.setItem('instashare:recent', '{not json');
        expect(loadRecent(now, s)).toEqual([]);
        s.setItem('instashare:recent', JSON.stringify([{ id: 1 }, { id: 'ok', name: 'Ok', kind: 'folder', openedAt: 'x' }]));
        expect(loadRecent(now, s).map((r) => r.id)).toEqual(['ok']);
        expect(loadRecent(now, null)).toEqual([]);
        expect(() => rememberShare({ id: 'a', name: 'A', kind: 'folder' }, now, null)).not.toThrow();
    });
});
