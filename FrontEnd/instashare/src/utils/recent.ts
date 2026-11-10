import type { FileKind } from '@/models/fileEntry';

/** A share this browser has opened, for the home page's "Recently opened" list. */
export interface RecentShare {
    id: string;
    name: string;
    kind: FileKind | 'folder';
    openedAt: string;
    expiresUtc?: string;
}

const KEY = 'instashare:recent';
export const MAX_RECENT = 8;

type StorageLike = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

// Private mode or blocked storage throws on access; the list then just stays empty.
function browserStorage(): StorageLike | null {
    try {
        return window.localStorage;
    } catch {
        return null;
    }
}

const isRecentShare = (r: unknown): r is RecentShare =>
    !!r && typeof r === 'object'
    && typeof (r as RecentShare).id === 'string'
    && typeof (r as RecentShare).name === 'string'
    && typeof (r as RecentShare).kind === 'string'
    && typeof (r as RecentShare).openedAt === 'string';

/** Newest first. Expired shares are left out, since their links no longer work. */
export function loadRecent(now = Date.now(), storage: StorageLike | null = browserStorage()): RecentShare[] {
    if (!storage) return [];
    try {
        const parsed: unknown = JSON.parse(storage.getItem(KEY) ?? '[]');
        if (!Array.isArray(parsed)) return [];
        return parsed.filter(isRecentShare).filter((r) => !r.expiresUtc || !(Date.parse(r.expiresUtc) <= now));
    } catch {
        return [];
    }
}

/** Puts a share at the top of the list, replacing any older entry for it. */
export function rememberShare(entry: Omit<RecentShare, 'openedAt'>, now = Date.now(), storage: StorageLike | null = browserStorage()) {
    const list = [{ ...entry, openedAt: new Date(now).toISOString() }, ...loadRecent(now, storage).filter((r) => r.id !== entry.id)];
    save(list.slice(0, MAX_RECENT), storage);
}

export function forgetShare(id: string, now = Date.now(), storage: StorageLike | null = browserStorage()) {
    save(loadRecent(now, storage).filter((r) => r.id !== id), storage);
}

export function clearRecent(storage: StorageLike | null = browserStorage()) {
    try {
        storage?.removeItem(KEY);
    } catch {
        // Nothing to clear.
    }
}

function save(list: RecentShare[], storage: StorageLike | null) {
    try {
        if (list.length === 0) storage?.removeItem(KEY);
        else storage?.setItem(KEY, JSON.stringify(list));
    } catch {
        // Full or blocked storage; the list just isn't kept.
    }
}
