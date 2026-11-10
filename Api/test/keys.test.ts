import { describe, expect, it } from 'vitest';
import worker, { type Env } from '../src/index';
import { hashKey, newKey, type KeyRecord } from '../src/keys';

// In-memory stand-ins for KV and the quota Durable Object.
class FakeKv {
    data = new Map<string, string>();
    async get(key: string, type?: string) {
        const v = this.data.get(key);
        return v === undefined ? null : type === 'json' ? JSON.parse(v) : v;
    }
}

class FakeQuota {
    used = new Map<string, number>(); // hash|day -> bytes
    idFromName(name: string) {
        return name;
    }
    get(id: string) {
        return {
            reserve: async (bytes: number, limit: number, day: string) => {
                const k = `${id}|${day}`;
                const used = this.used.get(k) ?? 0;
                if (used + bytes > limit) return { ok: false, used, limit };
                this.used.set(k, used + bytes);
                return { ok: true, used: used + bytes, limit };
            },
        };
    }
}

class FakeBucket {
    objects = new Map<string, { customMetadata?: Record<string, string> }>();
    async put(key: string, _body: string, options?: { customMetadata?: Record<string, string> }) {
        this.objects.set(key, { ...options });
    }
    async head(key: string) {
        const o = this.objects.get(key);
        return o ? { key, customMetadata: o.customMetadata } : null;
    }
}

const GB = 1024 ** 3;

async function setup({ require = true, dailyBytes = 50 * GB } = {}) {
    const kv = new FakeKv();
    const key = newKey();
    const record: KeyRecord = { name: 'Mohit', dailyBytes, created: '2026-11-10T00:00:00Z' };
    kv.data.set('key:' + (await hashKey(key)), JSON.stringify(record));
    const quota = new FakeQuota();
    const env = {
        BUCKET: new FakeBucket() as unknown as R2Bucket,
        BUCKET_NAME: 'instashare',
        MAX_OBJECT_BYTES: String(10 * GB),
        R2_ACCOUNT_ID: 'acct',
        R2_ACCESS_KEY_ID: 'AKID',
        R2_SECRET_ACCESS_KEY: 'secret',
        TOKEN_SECRET: 'token-secret',
        API_KEYS: kv as unknown as KVNamespace,
        QUOTA: quota as unknown as Env['QUOTA'],
        REQUIRE_API_KEY: require ? 'true' : 'false',
    } satisfies Env;
    return { env, key, quota };
}

async function call(env: Env, path: string, body: unknown, key?: string) {
    const headers: Record<string, string> = { 'content-type': 'application/json' };
    if (key) headers.authorization = `Bearer ${key}`;
    const res = await worker.fetch(new Request(`https://api.test${path}`, { method: 'POST', body: JSON.stringify(body), headers }), env);
    return { status: res.status, body: (await res.json()) as Record<string, any> };
}

const createFolder = { kind: 'folder', expiresInHours: 24 };

describe('API keys', () => {
    it('refuses to create a share without a key', async () => {
        const { env } = await setup();
        const res = await call(env, '/v1/shares', createFolder);
        expect(res.status).toBe(401);
        expect(res.body.error).toMatch(/API key/);
    });

    it('refuses a wrong or revoked key', async () => {
        const { env } = await setup();
        expect((await call(env, '/v1/shares', createFolder, newKey())).status).toBe(401);
        expect((await call(env, '/v1/shares', createFolder, 'not-even-a-key')).status).toBe(401);
    });

    it('accepts a valid key', async () => {
        const { env, key } = await setup();
        const res = await call(env, '/v1/shares', createFolder, key);
        expect(res.status).toBe(200);
        expect(res.body.token).toBeTruthy();
    });

    it('a share token alone is no longer enough to get upload links', async () => {
        const { env, key } = await setup();
        const share = (await call(env, '/v1/shares', createFolder, key)).body;
        const objects = [{ key: `${share.prefix}a.txt`, size: 10, contentType: 'text/plain' }];

        expect((await call(env, '/v1/sign', { ...share, objects })).status).toBe(401);
        expect((await call(env, '/v1/sign', { ...share, objects }, key)).status).toBe(200);
    });

    it('lets requests without a key through while the requirement is off', async () => {
        const { env } = await setup({ require: false });
        expect((await call(env, '/v1/shares', createFolder)).status).toBe(200);
    });

    it('a key still has to be valid while the requirement is off', async () => {
        const { env } = await setup({ require: false });
        expect((await call(env, '/v1/shares', createFolder, newKey())).status).toBe(401);
    });
});

describe('daily cap', () => {
    it('counts signed bytes and stops at the cap, without counting the refused batch', async () => {
        const { env, key, quota } = await setup({ dailyBytes: 100 });
        const share = (await call(env, '/v1/shares', createFolder, key)).body;
        const sign = (sizes: number[]) => call(env, '/v1/sign', {
            ...share, objects: sizes.map((size, i) => ({ key: `${share.prefix}f${i}-${size}`, size, contentType: 'x/y' })),
        }, key);

        expect((await sign([40, 40])).status).toBe(200);   // 80 of 100
        const over = await sign([30]);                      // would be 110
        expect(over.status).toBe(429);
        expect(over.body.error).toMatch(/Daily upload limit/);
        expect([...quota.used.values()]).toEqual([80]);     // the refused batch counted nothing
        expect((await sign([20])).status).toBe(200);        // exactly 100 is fine
    });

    it('counts a large file once, when its multipart upload starts', async () => {
        const { env, key, quota } = await setup({ dailyBytes: 5 * GB });
        const share = (await call(env, '/v1/shares', createFolder, key)).body;
        // Starting the upload would also call R2; we only need the cap check to refuse first.
        const res = await call(env, '/v1/multipart/create', {
            ...share, key: `${share.prefix}huge.bin`, size: 6 * GB, contentType: 'application/octet-stream',
        }, key);
        expect(res.status).toBe(429);
        expect(quota.used.size).toBe(0);
    });

    it('the cap is per key', async () => {
        const first = await setup({ dailyBytes: 50 });
        const share = (await call(first.env, '/v1/shares', createFolder, first.key)).body;
        const objects = [{ key: `${share.prefix}a`, size: 50, contentType: 'x/y' }];
        expect((await call(first.env, '/v1/sign', { ...share, objects }, first.key)).status).toBe(200);

        // A second person on the same Worker has their own allowance.
        const otherKey = newKey();
        await (first.env.API_KEYS as unknown as FakeKv).data.set('key:' + (await hashKey(otherKey)),
            JSON.stringify({ name: 'Other', dailyBytes: 50, created: '2026-11-10T00:00:00Z' }));
        const otherShare = (await call(first.env, '/v1/shares', createFolder, otherKey)).body;
        const otherObjects = [{ key: `${otherShare.prefix}b`, size: 50, contentType: 'x/y' }];
        expect((await call(first.env, '/v1/sign', { ...otherShare, objects: otherObjects }, otherKey)).status).toBe(200);
    });
});

describe('newKey', () => {
    it('is long, URL-safe and unique', () => {
        const a = newKey();
        expect(a).toMatch(/^isk_[A-Za-z0-9_-]{43}$/);
        expect(newKey()).not.toBe(a);
    });
});
