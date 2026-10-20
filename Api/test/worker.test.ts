import { describe, expect, it } from 'vitest';
import worker, { deleteExpiredShares, deleteLegacyShares, type Env } from '../src/index';

// Just enough of R2Bucket for the Worker's own calls. Presigned uploads go to R2 directly and aren't covered here.
class FakeBucket {
  objects = new Map<string, { body: string; uploaded?: Date; customMetadata?: Record<string, string>; httpMetadata?: unknown }>();

  async put(key: string, body: string, options?: { customMetadata?: Record<string, string>; httpMetadata?: unknown }) {
    this.objects.set(key, { body, uploaded: new Date(), ...options });
    return {};
  }

  async head(key: string) {
    const o = this.objects.get(key);
    return o ? { key, customMetadata: o.customMetadata } : null;
  }

  async delete(keys: string | string[]) {
    for (const key of Array.isArray(keys) ? keys : [keys]) this.objects.delete(key);
  }

  async list({ prefix = '', cursor, limit = 1000, delimiter }: { prefix?: string; cursor?: string; limit?: number; delimiter?: string }) {
    const keys = [...this.objects.keys()].filter((k) => k.startsWith(prefix)).sort();
    const start = cursor ? Number(cursor) : 0;
    if (delimiter) {
      const folders = keys.filter((k) => k.indexOf(delimiter, prefix.length) >= 0).map((k) => k.slice(0, k.indexOf(delimiter, prefix.length) + 1));
      const prefixes = [...new Set(folders)];
      return { objects: [], delimitedPrefixes: prefixes.slice(start, start + limit), truncated: start + limit < prefixes.length, cursor: String(start + limit) };
    }
    const page = keys.slice(start, start + limit);
    return {
      objects: page.map((key) => ({ key, uploaded: this.objects.get(key)!.uploaded ?? new Date(), customMetadata: this.objects.get(key)!.customMetadata })),
      delimitedPrefixes: [],
      truncated: start + limit < keys.length,
      cursor: String(start + limit),
    };
  }
}

function makeEnv() {
  const bucket = new FakeBucket();
  const env = {
    BUCKET: bucket as unknown as R2Bucket,
    BUCKET_NAME: 'instashare',
    MAX_OBJECT_BYTES: String(1024 * 1024),
    R2_ACCOUNT_ID: 'acct',
    R2_ACCESS_KEY_ID: 'AKID',
    R2_SECRET_ACCESS_KEY: 'secret',
    TOKEN_SECRET: 'token-secret',
  } satisfies Env;
  return { env, bucket };
}

async function call(env: Env, path: string, body: unknown) {
  const response = await worker.fetch(new Request(`https://api.test${path}`, { method: 'POST', body: JSON.stringify(body) }), env);
  return { status: response.status, body: (await response.json()) as Record<string, any> };
}

describe('worker', () => {
  it('creates a share with a token and an expiry marker', async () => {
    const { env, bucket } = makeEnv();
    const { status, body } = await call(env, '/v1/shares', { kind: 'file', name: 'a b.txt', expiresInHours: 24 });

    expect(status).toBe(200);
    expect(body.prefix).toMatch(/^a b\.txt-[0-9a-f-]{36}\/$/);
    expect(body.token).toBeTruthy();
    const marker = bucket.objects.get('_shares/' + body.prefix.slice(0, -1))!;
    expect(Date.parse(marker.customMetadata!.expires) - Date.now()).toBeGreaterThan(23.9 * 3600_000);
  });

  it('signs uploads only inside the share and within the size limit', async () => {
    const { env } = makeEnv();
    const share = (await call(env, '/v1/shares', { kind: 'folder', expiresInHours: 1 })).body;

    const ok = await call(env, '/v1/sign', { ...share, objects: [{ key: `${share.prefix}a #1.txt`, size: 10, contentType: 'text/plain' }] });
    expect(ok.status).toBe(200);
    expect(ok.body.urls[0]).toMatch(/^https:\/\/acct\.r2\.cloudflarestorage\.com\/instashare\/[0-9a-f-]+\/a%20%231\.txt\?/);

    expect((await call(env, '/v1/sign', { ...share, objects: [{ key: 'elsewhere/x', size: 1, contentType: 'text/plain' }] })).status).toBe(400);
    expect((await call(env, '/v1/sign', { ...share, objects: [{ key: `${share.prefix}big`, size: 2 * 1024 * 1024, contentType: 'x/y' }] })).status).toBe(413);
  });

  it('rejects a wrong token', async () => {
    const { env } = makeEnv();
    const share = (await call(env, '/v1/shares', { kind: 'folder', expiresInHours: 1 })).body;
    const other = (await call(env, '/v1/shares', { kind: 'folder', expiresInHours: 1 })).body;

    const res = await call(env, '/v1/index', { prefix: share.prefix, token: other.token });
    expect(res.status).toBe(403);
  });

  it('signs a reusable index URL', async () => {
    const { env } = makeEnv();
    const share = (await call(env, '/v1/shares', { kind: 'folder', expiresInHours: 1 })).body;
    const { status, body } = await call(env, '/v1/index', share);
    expect(status).toBe(200);
    expect(body.url).toContain(`/instashare/${share.prefix}index.json?`);
    expect(new URL(body.url).searchParams.get('X-Amz-SignedHeaders')).toBe('content-type;host');
  });

  it('deletes the whole share and nothing else', async () => {
    const { env, bucket } = makeEnv();
    const share = (await call(env, '/v1/shares', { kind: 'folder', expiresInHours: 1 })).body;
    bucket.objects.set(`${share.prefix}index.json`, { body: '[]' });
    bucket.objects.set(`${share.prefix}a`, { body: 'x' });
    bucket.objects.set('someone-else/keep', { body: 'x' });

    expect((await call(env, '/v1/shares/delete', share)).status).toBe(200);
    expect([...bucket.objects.keys()]).toEqual(['someone-else/keep']);
  });

  it('refuses uploads after a share expires', async () => {
    const { env, bucket } = makeEnv();
    const share = (await call(env, '/v1/shares', { kind: 'folder', expiresInHours: 1 })).body;
    bucket.objects.get('_shares/' + share.prefix.slice(0, -1))!.customMetadata!.expires = new Date(Date.now() - 1000).toISOString();

    expect((await call(env, '/v1/index', share)).status).toBe(410);
  });

  it('cleans up expired shares on the schedule and leaves the rest', async () => {
    const { env, bucket } = makeEnv();
    const old = (await call(env, '/v1/shares', { kind: 'folder', expiresInHours: 1 })).body;
    const fresh = (await call(env, '/v1/shares', { kind: 'folder', expiresInHours: 168 })).body;
    bucket.objects.set(`${old.prefix}file`, { body: 'x' });
    bucket.objects.set(`${fresh.prefix}file`, { body: 'x' });

    await deleteExpiredShares(env, new Date(Date.now() + 2 * 3600_000));

    expect(bucket.objects.has(`${old.prefix}file`)).toBe(false);
    expect(bucket.objects.has(`${fresh.prefix}file`)).toBe(true);
  });

  it('answers bad input with a JSON error, not a crash', async () => {
    const { env } = makeEnv();
    const response = await worker.fetch(new Request('https://api.test/v1/shares', { method: 'POST', body: 'not json' }), env);
    expect(response.status).toBe(400);
    expect(await response.json()).toHaveProperty('error');
  });

  it('sweeps old shares from before the Worker, and nothing live or recent', async () => {
    const { env, bucket } = makeEnv();
    const uuid = (n: number) => `00000000-0000-4000-8000-00000000000${n}`;
    const daysAgo = (d: number) => new Date(Date.now() - d * 24 * 3600_000);
    bucket.objects.set(`${uuid(1)}/Photos/a.jpg`, { body: 'x', uploaded: daysAgo(30) }); // old folder share
    bucket.objects.set(`r.pdf-${uuid(2)}/r.pdf`, { body: 'x', uploaded: daysAgo(3) });    // old file share
    bucket.objects.set(`${uuid(3)}/Docs/a.txt`, { body: 'x', uploaded: daysAgo(1) });     // recent, keep
    bucket.objects.set('not-a-share/file', { body: 'x', uploaded: daysAgo(30) });         // unknown, keep
    const live = (await call(env, '/v1/shares', { kind: 'folder', expiresInHours: 168 })).body;
    bucket.objects.set(`${live.prefix}a`, { body: 'x', uploaded: daysAgo(30) });          // has a marker, keep

    await deleteLegacyShares(env, new Date());

    const left = [...bucket.objects.keys()].filter((k) => !k.startsWith('_shares/')).sort();
    expect(left).toEqual([`${uuid(3)}/Docs/a.txt`, `${live.prefix}a`, 'not-a-share/file'].sort());
  });
});
