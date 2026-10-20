import { useEffect, useRef, useState } from 'react';
import type { ShareIndex } from '@/models/fileEntry';
import { parseIndex } from '@/utils/utils';

export const CDN_BASE_URL = import.meta.env.VITE_CDN_BASE_URL ?? 'https://cdn.instashare.mohitkumarverma.com';

const POLL_MS = 3000;

export type ShareState =
    | { status: 'loading' }
    | { status: 'missing' }  // Never existed, expired, or deleted
    | { status: 'error' }    // Network or server trouble; worth a retry
    | { status: 'ready'; index: ShareIndex };

/**
 * Loads a share's index.json, and keeps polling only while some file is still uploading.
 */
export function useShare(shareId: string) {
    const [state, setState] = useState<ShareState>({ status: 'loading' });
    const [attempt, setAttempt] = useState(0);
    const timer = useRef<number | undefined>(undefined);

    useEffect(() => {
        let cancelled = false;
        const controller = new AbortController();
        setState({ status: 'loading' });

        const load = async () => {
            try {
                // The query string skips any copy the CDN or browser cached while the upload was running.
                const res = await fetch(`${CDN_BASE_URL}/${encodeURIComponent(shareId)}/index.json?t=${Date.now()}`, { signal: controller.signal });
                if (cancelled) return;
                if (res.status === 404 || res.status === 403) {
                    setState({ status: 'missing' });
                    return;
                }
                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                const index = parseIndex(await res.json());
                if (cancelled) return;
                setState({ status: 'ready', index });
                if (index.files.some((f) => f.status === 'uploading'))
                    timer.current = window.setTimeout(load, POLL_MS);
            } catch (err) {
                if (cancelled || controller.signal.aborted) return;
                console.error('Failed to load index.json', err);
                // Keep showing what we have if a later poll fails; only show the error if nothing loaded yet.
                setState((prev) => (prev.status === 'ready' ? prev : { status: 'error' }));
                timer.current = window.setTimeout(load, POLL_MS * 2);
            }
        };
        load();

        return () => {
            cancelled = true;
            controller.abort();
            window.clearTimeout(timer.current);
        };
    }, [shareId, attempt]);

    return { state, retry: () => setAttempt((a) => a + 1) };
}

/** Re-renders every minute, so "expires in" counts down without a reload. */
export function useNow(intervalMs = 60_000) {
    const [now, setNow] = useState(() => Date.now());
    useEffect(() => {
        const id = window.setInterval(() => setNow(Date.now()), intervalMs);
        return () => window.clearInterval(id);
    }, [intervalMs]);
    return now;
}
