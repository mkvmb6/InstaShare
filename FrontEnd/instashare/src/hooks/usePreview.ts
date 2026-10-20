import { useCallback, useState } from 'react';
import type { Item } from '@/models/fileEntry';

type FileItem = Extract<Item, { type: 'file' }>;

/** Which file the preview dialog shows, as an index into the folder's previewable files. */
export function usePreview(items: FileItem[]) {
    const [index, setIndex] = useState<number | null>(null);
    const open = useCallback((path: string) => {
        const i = items.findIndex((it) => it.path === path);
        if (i >= 0) setIndex(i);
    }, [items]);
    return { index, setIndex, open };
}
