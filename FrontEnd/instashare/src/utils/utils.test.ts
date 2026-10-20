import { describe, expect, it } from 'vitest';
import type { FileEntry } from '@/models/fileEntry';
import { encodePath, formatBytes, formatDuration, getFileKind, itemsAt, parseIndex, smartCompare, summarize } from './utils';

const file = (path: string, size = 1, status: FileEntry['status'] = 'uploaded'): FileEntry => ({ path, size, status, url: `https://cdn/${path}` });

describe('itemsAt', () => {
    const files = [
        file('Share/a.txt'),
        file('Share/Photos/1.jpg', 10),
        file('Share/Photos/2.jpg', 20, 'uploading'),
        file('Share/Photos 2024/x.jpg', 5),
        file('Share/Photos/Raw/3.cr2', 30, 'failed'),
    ];

    it('lists direct files and subfolders with totals', () => {
        const items = itemsAt(files, 'Share');
        expect(items.map((i) => `${i.type}:${i.name}`)).toEqual(['folder:Photos', 'folder:Photos 2024', 'file:a.txt']);
        const photos = items[0];
        expect(photos).toMatchObject({ count: 3, size: 60, uploading: 1, failed: 1 });
    });

    it('does not mix up folders that share a prefix', () => {
        const items = itemsAt(files, 'Share/Photos');
        expect(items.map((i) => i.name)).toEqual(['Raw', '1.jpg', '2.jpg']);
    });

    it('handles the root and backslash paths from old uploads', () => {
        expect(itemsAt([file('a.txt'), file('dir\\b.txt')], '').map((i) => i.name)).toEqual(['dir', 'a.txt']);
    });
});

describe('summarize', () => {
    it('counts statuses below a folder only', () => {
        const files = [file('A/x', 1), file('A/y', 2, 'uploading'), file('AB/z', 4)];
        expect(summarize(files, 'A')).toEqual({ count: 2, size: 3, uploaded: 1, uploading: 1, failed: 0 });
    });
});

describe('parseIndex', () => {
    it('reads the old bare-array format', () => {
        expect(parseIndex([file('a')]).files).toHaveLength(1);
    });

    it('reads the new object format', () => {
        const index = parseIndex({ name: 'Photos', kind: 'folder', expiresUtc: '2026-01-01T00:00:00Z', files: [file('a')] });
        expect(index.name).toBe('Photos');
        expect(index.files).toHaveLength(1);
    });

    it('rejects anything else', () => {
        expect(() => parseIndex({ nope: true })).toThrow();
        expect(() => parseIndex('x')).toThrow();
    });
});

describe('formatting', () => {
    it('formats sizes', () => {
        expect(formatBytes(0)).toBe('0 B');
        expect(formatBytes(512)).toBe('512 B');
        expect(formatBytes(1536)).toBe('1.5 KB');
        expect(formatBytes(3 * 1024 ** 3)).toBe('3 GB');
    });

    it('formats durations', () => {
        expect(formatDuration(30_000)).toBe('1 min');
        expect(formatDuration(45 * 60_000)).toBe('45 min');
        expect(formatDuration(90 * 60_000)).toBe('1 h 30 min');
        expect(formatDuration(5 * 3600_000)).toBe('5 h');
        expect(formatDuration(28 * 3600_000)).toBe('1 day 4 h');
        expect(formatDuration(48 * 3600_000)).toBe('2 days');
    });

    it('sorts naturally', () => {
        expect(['file 10', 'File 2', 'file 1'].sort(smartCompare)).toEqual(['file 1', 'File 2', 'file 10']);
    });

    it('detects kinds', () => {
        expect(getFileKind('a.JPG')).toBe('image');
        expect(getFileKind('notes.md')).toBe('markdown');
        expect(getFileKind('song.mp3')).toBe('audio');
        expect(getFileKind('movie.mkv')).toBe('other'); // browsers can't play it
        expect(getFileKind('Makefile')).toBe('other');
    });

    it('encodes route paths per segment', () => {
        expect(encodePath('My Folder/a#1')).toBe('My%20Folder/a%231');
    });
});
