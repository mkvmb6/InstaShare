// API keys and the per-key daily upload cap.
//
// A key is shown once, when it's created. KV keeps only its SHA-256 hash, so nothing stored there can be used
// as a key. Every request that creates or uploads must carry a valid key; a share's token alone is no longer enough.

import { DurableObject } from 'cloudflare:workers';
import { HttpError } from './shares';

export const KEY_PREFIX = 'isk_';
const HASH_PREFIX = 'key:';
export const DEFAULT_DAILY_BYTES = 50 * 1024 ** 3;

export interface KeyRecord {
    name: string;
    /** Upload allowance per UTC day, in bytes. */
    dailyBytes: number;
    created: string;
}

export interface KeysEnv {
    API_KEYS: KVNamespace;
    QUOTA: DurableObjectNamespace<QuotaCounter>;
    /** "true" to refuse requests without a key. Anything else lets them through while clients are updated. */
    REQUIRE_API_KEY?: string;
}

/** The key's owner and limits, or null when the request may proceed without one (transition only). */
export type Caller = { hash: string; record: KeyRecord } | null;

export async function hashKey(key: string): Promise<string> {
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(key));
    return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('');
}

/** A new key: "isk_" and 32 random bytes, base64url. */
export function newKey(): string {
    const bytes = crypto.getRandomValues(new Uint8Array(32));
    return KEY_PREFIX + btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** Reads "Authorization: Bearer isk_..." and checks it against KV. */
export async function authenticate(request: Request, env: KeysEnv): Promise<Caller> {
    const header = request.headers.get('authorization') ?? '';
    const key = /^Bearer\s+(\S+)$/i.exec(header)?.[1];
    if (!key) {
        if (env.REQUIRE_API_KEY === 'true') throw new HttpError(401, 'This InstaShare needs an API key. Add yours in Settings.');
        return null;
    }
    // Cheap check first, so garbage never reaches KV.
    if (!key.startsWith(KEY_PREFIX) || key.length > 100) throw new HttpError(401, 'That API key isn\'t valid.');
    const hash = await hashKey(key);
    const record = await env.API_KEYS.get<KeyRecord>(HASH_PREFIX + hash, 'json');
    if (!record) throw new HttpError(401, 'That API key isn\'t valid, or has been revoked.');
    return { hash, record };
}

/** Counts bytes against the caller's daily allowance. Throws 429, counting nothing, if they wouldn't fit. */
export async function reserveBytes(env: KeysEnv, caller: Caller, bytes: number): Promise<void> {
    if (!caller || bytes <= 0) return;
    const counter = env.QUOTA.get(env.QUOTA.idFromName(caller.hash));
    const result = await counter.reserve(bytes, caller.record.dailyBytes, utcDay(new Date()));
    if (!result.ok)
        throw new HttpError(429, `Daily upload limit reached (${formatGb(result.limit)} GB a day). It resets at midnight UTC.`);
}

export const utcDay = (date: Date) => date.toISOString().slice(0, 10);

const formatGb = (bytes: number) => (bytes / 1024 ** 3).toFixed(bytes % 1024 ** 3 === 0 ? 0 : 1);

/**
 * One per key. A Durable Object runs one request at a time, so parallel uploads can't race past the cap.
 * Stores a single row per day; older days are dropped as new ones arrive.
 */
export class QuotaCounter extends DurableObject<unknown> {
    constructor(ctx: DurableObjectState, env: unknown) {
        super(ctx, env);
        ctx.storage.sql.exec('CREATE TABLE IF NOT EXISTS usage (day TEXT PRIMARY KEY, bytes INTEGER NOT NULL)');
    }

    reserve(bytes: number, limit: number, day: string): { ok: boolean; used: number; limit: number } {
        const sql = this.ctx.storage.sql;
        const row = sql.exec<{ bytes: number }>('SELECT bytes FROM usage WHERE day = ?', day).toArray()[0];
        const used = row?.bytes ?? 0;
        if (used + bytes > limit) return { ok: false, used, limit };
        sql.exec('INSERT INTO usage (day, bytes) VALUES (?, ?) ON CONFLICT(day) DO UPDATE SET bytes = excluded.bytes', day, used + bytes);
        sql.exec('DELETE FROM usage WHERE day < ?', day);
        return { ok: true, used: used + bytes, limit };
    }

    usage(day: string): number {
        return this.ctx.storage.sql.exec<{ bytes: number }>('SELECT bytes FROM usage WHERE day = ?', day).toArray()[0]?.bytes ?? 0;
    }
}
