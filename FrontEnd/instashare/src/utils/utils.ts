import type { FileEntry, FileKind, Item, ShareIndex } from '@/models/fileEntry';

export const formatBytes = (bytes: number = 0) => {
    if (bytes <= 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.min(Math.floor(Math.log(bytes) / Math.log(k)), sizes.length - 1);
    return parseFloat((bytes / Math.pow(k, i)).toFixed(i === 0 ? 0 : 1)) + ' ' + sizes[i];
};

/** "45 min", "5 h", "1 day 4 h". */
export function formatDuration(ms: number) {
    const minutes = Math.max(1, Math.round(ms / 60_000));
    if (minutes < 60) return `${minutes} min`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return minutes % 60 === 0 || hours >= 6 ? `${hours} h` : `${hours} h ${minutes % 60} min`;
    const days = Math.floor(hours / 24);
    const rest = hours % 24;
    return `${days} ${days === 1 ? 'day' : 'days'}${rest ? ` ${rest} h` : ''}`;
}

/** Natural order, so "file 2" comes before "file 10". */
const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
export const smartCompare = (a: string, b: string) => collator.compare(a, b);

const KINDS: Record<string, FileKind> = {};
const register = (kind: FileKind, exts: string) => exts.split(' ').forEach((e) => (KINDS[e] = kind));
// Only formats browsers can actually play or show; anything else is download-only.
register('image', 'jpg jpeg png gif bmp svg webp ico avif');
register('video', 'mp4 webm mov m4v ogv');
register('audio', 'mp3 wav ogg oga m4a aac flac opus');
register('pdf', 'pdf');
register('markdown', 'md markdown');
register('text', 'txt csv tsv json xml log yml yaml ini cfg conf toml js ts tsx jsx css html htm py cs java c cpp h go rs rb php sh ps1 bat sql');
register('archive', 'zip rar 7z tar gz tgz bz2 xz');

export function getFileKind(name: string): FileKind {
    const dot = name.lastIndexOf('.');
    return dot < 0 ? 'other' : KINDS[name.slice(dot + 1).toLowerCase()] ?? 'other';
}

export const isPreviewable = (kind: FileKind) => kind !== 'other' && kind !== 'archive';

/** Accepts both index formats: the bare array from older uploads, and the object from desktop app 2.0+. */
export function parseIndex(data: unknown): ShareIndex {
    if (Array.isArray(data)) return { files: data as FileEntry[] };
    if (data && typeof data === 'object' && Array.isArray((data as ShareIndex).files)) return data as ShareIndex;
    throw new Error('Unexpected index format.');
}

/** Paths always use '/'; very old uploads may contain backslashes. */
export const normalizePath = (path: string) => path.replace(/\\/g, '/').replace(/^\/+/, '');

/**
 * Splits the share into what sits directly in `currentPath`: its subfolders (with totals) and its files.
 * `currentPath` is '' for the root, otherwise a folder path without trailing slash.
 */
export function itemsAt(files: FileEntry[], currentPath: string): Item[] {
    const prefix = currentPath ? currentPath + '/' : '';
    const folders = new Map<string, Extract<Item, { type: 'folder' }>>();
    const fileItems: Item[] = [];

    for (const entry of files) {
        const path = normalizePath(entry.path);
        // The trailing '/' matters: "Photos" must not match files in "Photos 2024".
        if (!path.startsWith(prefix)) continue;
        const rest = path.slice(prefix.length);
        const slash = rest.indexOf('/');
        if (slash < 0) {
            fileItems.push({ type: 'file', name: rest, path, kind: getFileKind(rest), entry });
            continue;
        }
        const name = rest.slice(0, slash);
        const folder = folders.get(name) ?? { type: 'folder', name, path: prefix + name, count: 0, size: 0, uploading: 0, failed: 0 };
        folder.count++;
        folder.size += entry.size ?? 0;
        if (entry.status === 'uploading') folder.uploading++;
        if (entry.status === 'failed') folder.failed++;
        folders.set(name, folder);
    }
    return [...folders.values(), ...fileItems];
}

/** Totals for everything at or below `currentPath`. */
export function summarize(files: FileEntry[], currentPath = '') {
    const prefix = currentPath ? currentPath + '/' : '';
    let count = 0, size = 0, uploaded = 0, uploading = 0, failed = 0;
    for (const f of files) {
        if (!normalizePath(f.path).startsWith(prefix)) continue;
        count++;
        size += f.size ?? 0;
        if (f.status === 'uploaded') uploaded++;
        else if (f.status === 'uploading') uploading++;
        else failed++;
    }
    return { count, size, uploaded, uploading, failed };
}

/** Percent-encodes each path segment for use in a router path, keeping the slashes. */
export const encodePath = (path: string) => path.split('/').map(encodeURIComponent).join('/');
