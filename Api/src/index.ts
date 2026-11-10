// InstaShare API. Holds the R2 keys so the desktop app doesn't have to.
//
// File bytes never pass through here: the app gets short-lived presigned URLs and uploads straight to R2.
// Each share gets a token at creation; every later call for that share must present it.
// Creating and uploading also need an API key (see keys.ts), and count against that key's daily cap.

import { presignUrl, signHeaders, type Credentials } from './sigv4';
import { authenticate, reserveBytes, type Caller, type KeysEnv } from './keys';

export { QuotaCounter } from './keys';
import {
  HttpError, LEGACY_EXPIRY_MS, MAX_SINGLE_PUT_BYTES, MARKER_PREFIX, SHARE_PREFIX,
  checkBatch, checkExpiry, checkHeader, checkKey, checkPartNumber, checkPrefix, checkSize,
  markerKey, newPrefix, prefixFromMarker, shareToken, verifyToken,
} from './shares';

export interface Env extends KeysEnv {
  BUCKET: R2Bucket;
  BUCKET_NAME: string;
  /** Largest file anyone can share, in bytes. */
  MAX_OBJECT_BYTES: string;
  // Secrets (wrangler secret put ...)
  R2_ACCOUNT_ID: string;
  R2_ACCESS_KEY_ID: string;
  R2_SECRET_ACCESS_KEY: string;
  TOKEN_SECRET: string;
  /** Optional: limits share creation per IP. See wrangler.jsonc. */
  CREATE_LIMITER?: RateLimit;
}

const PUT_URL_TTL_SECONDS = 6 * 3600;
const PART_URL_TTL_SECONDS = 12 * 3600;
const MAX_BODY_BYTES = 256 * 1024;
const CLEANUP_SHARES_PER_RUN = 50;
const LEGACY_SHARES_PER_RUN = 20;

type Body = Record<string, unknown>;

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    try {
      return await route(request, env);
    } catch (error) {
      if (error instanceof HttpError) return json({ error: error.message }, error.status);
      console.error(error);
      return json({ error: 'Something went wrong. Try again.' }, 500);
    }
  },

  async scheduled(_controller: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    ctx.waitUntil(deleteExpiredShares(env, new Date()));
  },
} satisfies ExportedHandler<Env>;

async function route(request: Request, env: Env): Promise<Response> {
  const { pathname } = new URL(request.url);
  if (request.method === 'GET' && pathname === '/') return new Response('InstaShare API');
  if (request.method !== 'POST') throw new HttpError(405, 'Use POST.');

  const body = await readBody(request);
  // Deleting and cleaning up need only the share's token, so people can always remove what they shared.
  if (pathname === '/v1/shares/delete') return json(await deleteShareRequest(env, body));
  if (pathname === '/v1/multipart/abort') return json(await abortMultipart(env, body));

  const caller = await authenticate(request, env);
  switch (pathname) {
    case '/v1/shares':
      return json(await createShare(request, env, body));
    case '/v1/sign':
      return json(await signPuts(env, body, caller));
    case '/v1/index':
      return json(await signIndex(env, body));
    case '/v1/multipart/create':
      return json(await createMultipart(env, body, caller));
    case '/v1/multipart/sign':
      return json(await signParts(env, body));
    case '/v1/multipart/complete':
      return json(await completeMultipart(env, body));
    default:
      throw new HttpError(404, 'Not found.');
  }
}

async function createShare(request: Request, env: Env, body: Body) {
  if (env.CREATE_LIMITER) {
    const ip = request.headers.get('CF-Connecting-IP') ?? 'unknown';
    const { success } = await env.CREATE_LIMITER.limit({ key: ip });
    if (!success) throw new HttpError(429, 'Too many shares at once. Wait a minute and try again.');
  }

  const hours = checkExpiry(body.expiresInHours);
  const prefix = newPrefix(body.kind, body.name, crypto.randomUUID());
  const now = new Date();
  const expires = new Date(now.getTime() + hours * 3600_000);
  await env.BUCKET.put(markerKey(prefix), '', {
    customMetadata: { created: now.toISOString(), expires: expires.toISOString() },
  });
  return { prefix, token: await shareToken(env.TOKEN_SECRET, prefix), expiresUtc: expires.toISOString() };
}

/** Presigned PUTs for whole files. Content-Length is signed, so each URL takes exactly the size asked for. */
async function signPuts(env: Env, body: Body, caller: Caller) {
  const prefix = await authorize(env, body);
  const objects = checkBatch<Body>(body.objects, 'objects');
  const max = Math.min(maxObjectBytes(env), MAX_SINGLE_PUT_BYTES);
  // Check every entry first, then count the batch against the cap in one go.
  const checked = objects.map((o) => ({ o, key: checkKey(prefix, o.key), size: checkSize(o.size, max) }));
  // A link is signed for an exact size, so what's counted here is what can be uploaded. The preview
  // picture and re-signed links count again; that slightly overstates use, and never understates it.
  await reserveBytes(env, caller, checked.reduce((sum, c) => sum + c.size, 0));
  const now = new Date();
  const urls = await Promise.all(checked.map(({ o, key, size }) => {
    const headers: Record<string, string> = {
      'content-length': String(size),
      'content-type': checkHeader(o.contentType, 'contentType', true)!,
    };
    const disposition = checkHeader(o.contentDisposition, 'contentDisposition', false);
    if (disposition) headers['content-disposition'] = disposition;
    return presignUrl(credentials(env), { method: 'PUT', host: r2Host(env), path: objectPath(env, key), headers, now }, PUT_URL_TTL_SECONDS);
  }));
  return { urls };
}

/**
 * A reusable URL for the viewer's file list. The app rewrites index.json every second or so during an upload,
 * so it asks for one URL per share and keeps it, rather than calling the Worker for each write.
 * Its size isn't signed (it grows as files finish), but it can only ever overwrite that one object.
 */
async function signIndex(env: Env, body: Body) {
  const prefix = await authorize(env, body);
  const url = await presignUrl(credentials(env), {
    method: 'PUT', host: r2Host(env), path: objectPath(env, `${prefix}index.json`), now: new Date(),
    headers: { 'content-type': 'application/json' },
  }, PUT_URL_TTL_SECONDS);
  return { url };
}

async function createMultipart(env: Env, body: Body, caller: Caller) {
  const prefix = await authorize(env, body);
  const key = checkKey(prefix, body.key);
  // A large file counts once, here; its parts don't count again.
  await reserveBytes(env, caller, checkSize(body.size, maxObjectBytes(env)));
  const headers: Record<string, string> = { 'content-type': checkHeader(body.contentType, 'contentType', true)! };
  const disposition = checkHeader(body.contentDisposition, 'contentDisposition', false);
  if (disposition) headers['content-disposition'] = disposition;

  const text = await s3(env, 'POST', key, { uploads: '' }, headers);
  const uploadId = /<UploadId>([^<]+)<\/UploadId>/.exec(text)?.[1];
  if (!uploadId) throw new Error(`No UploadId in response: ${text.slice(0, 200)}`);
  return { uploadId };
}

async function signParts(env: Env, body: Body) {
  const prefix = await authorize(env, body);
  const key = checkKey(prefix, body.key);
  const uploadId = checkUploadId(body.uploadId);
  const parts = checkBatch<Body>(body.parts, 'parts');
  const now = new Date();
  const urls = await Promise.all(parts.map((p) => {
    const partNumber = checkPartNumber(p.partNumber);
    const size = checkSize(p.size, MAX_SINGLE_PUT_BYTES);
    return presignUrl(credentials(env), {
      method: 'PUT', host: r2Host(env), path: objectPath(env, key), now,
      query: { partNumber: String(partNumber), uploadId },
      headers: { 'content-length': String(size) },
    }, PART_URL_TTL_SECONDS);
  }));
  return { urls };
}

async function completeMultipart(env: Env, body: Body) {
  const prefix = await authorize(env, body);
  const key = checkKey(prefix, body.key);
  const uploadId = checkUploadId(body.uploadId);
  if (!Array.isArray(body.parts) || body.parts.length === 0) throw new HttpError(400, 'parts is required.');
  const parts = (body.parts as Body[])
    .map((p) => ({ partNumber: checkPartNumber(p.partNumber), etag: checkHeader(p.etag, 'etag', true)! }))
    .sort((a, b) => a.partNumber - b.partNumber);

  const xml = '<CompleteMultipartUpload>' +
    parts.map((p) => `<Part><PartNumber>${p.partNumber}</PartNumber><ETag>${escapeXml(p.etag)}</ETag></Part>`).join('') +
    '</CompleteMultipartUpload>';
  await s3(env, 'POST', key, { uploadId }, { 'content-type': 'application/xml' }, xml);
  return { ok: true };
}

async function abortMultipart(env: Env, body: Body) {
  const prefix = await authorize(env, body, { allowExpired: true });
  const key = checkKey(prefix, body.key);
  await s3(env, 'DELETE', key, { uploadId: checkUploadId(body.uploadId) }, {});
  return { ok: true };
}

async function deleteShareRequest(env: Env, body: Body) {
  const prefix = checkPrefix(body.prefix);
  await verifyToken(env.TOKEN_SECRET, prefix, body.token);
  await deleteShare(env, prefix);
  return { ok: true };
}

/** Deletes every object of a share, then its marker. Deletes are free in R2. */
export async function deleteShare(env: Env, prefix: string) {
  let cursor: string | undefined;
  do {
    const page = await env.BUCKET.list({ prefix, cursor, limit: 1000 });
    if (page.objects.length > 0) await env.BUCKET.delete(page.objects.map((o) => o.key));
    cursor = page.truncated ? page.cursor : undefined;
  } while (cursor);
  await env.BUCKET.delete(markerKey(prefix));
}

/**
 * Runs on the cron trigger. Shares expire even when their owner's app is never opened again.
 * Each run handles a bounded number, to stay inside the per-invocation subrequest limit.
 */
export async function deleteExpiredShares(env: Env, now: Date) {
  const expired: string[] = [];
  let cursor: string | undefined;
  do {
    const page = await env.BUCKET.list({ prefix: MARKER_PREFIX, cursor, limit: 1000, include: ['customMetadata'] });
    for (const marker of page.objects) {
      const expires = Date.parse(marker.customMetadata?.expires ?? '');
      // A marker without a readable date is treated as expired rather than kept forever.
      if (!(expires > now.getTime())) expired.push(prefixFromMarker(marker.key));
    }
    cursor = page.truncated && expired.length < CLEANUP_SHARES_PER_RUN ? page.cursor : undefined;
  } while (cursor);

  for (const prefix of expired.slice(0, CLEANUP_SHARES_PER_RUN)) {
    try {
      await deleteShare(env, prefix);
    } catch (error) {
      console.error(`Couldn't delete ${prefix}`, error); // Tried again next run
    }
  }

  await deleteLegacyShares(env, now);
}

/**
 * Shares uploaded before this Worker existed have no marker, and the app no longer holds keys to delete them.
 * They had a fixed 2-day expiry, so anything shaped like a share, without a marker and older than that, goes.
 * Every new share gets its marker before any upload URL is issued, so this never touches a live one.
 */
export async function deleteLegacyShares(env: Env, now: Date) {
  const marked = new Set<string>();
  let cursor: string | undefined;
  do {
    const page = await env.BUCKET.list({ prefix: MARKER_PREFIX, cursor, limit: 1000 });
    for (const marker of page.objects) marked.add(prefixFromMarker(marker.key));
    cursor = page.truncated ? page.cursor : undefined;
  } while (cursor);

  const candidates: string[] = [];
  cursor = undefined;
  do {
    const page = await env.BUCKET.list({ delimiter: '/', cursor, limit: 1000 });
    for (const prefix of page.delimitedPrefixes)
      if (SHARE_PREFIX.test(prefix) && !marked.has(prefix)) candidates.push(prefix);
    cursor = page.truncated && candidates.length < LEGACY_SHARES_PER_RUN ? page.cursor : undefined;
  } while (cursor);

  for (const prefix of candidates.slice(0, LEGACY_SHARES_PER_RUN)) {
    try {
      const page = await env.BUCKET.list({ prefix, limit: 1000 });
      const newest = Math.max(...page.objects.map((o) => o.uploaded.getTime()));
      if (now.getTime() - newest >= LEGACY_EXPIRY_MS) await deleteShare(env, prefix);
    } catch (error) {
      console.error(`Couldn't delete legacy share ${prefix}`, error);
    }
  }
}

/**
 * Checks the token, and that the share still exists. An expired share accepts no more uploads,
 * so nothing can be written under a prefix the cleanup has already removed.
 */
async function authorize(env: Env, body: Body, options: { allowExpired?: boolean } = {}): Promise<string> {
  const prefix = checkPrefix(body.prefix);
  await verifyToken(env.TOKEN_SECRET, prefix, body.token);
  if (!options.allowExpired) {
    const marker = await env.BUCKET.head(markerKey(prefix));
    const expires = Date.parse(marker?.customMetadata?.expires ?? '');
    if (!marker || !(expires > Date.now())) throw new HttpError(410, 'This share has expired.');
  }
  return prefix;
}

async function s3(env: Env, method: string, key: string, query: Record<string, string>, headers: Record<string, string>, body = '') {
  const signed = await signHeaders(credentials(env), { method, host: r2Host(env), path: objectPath(env, key), query, headers, now: new Date() }, body);
  const response = await fetch(signed.url, { method, headers: signed.headers, body: body || undefined });
  const text = await response.text();
  // CompleteMultipartUpload can answer 200 with an error document.
  if (!response.ok || text.includes('<Error>')) throw new HttpError(502, `Storage error: ${/<Message>([^<]*)<\/Message>/.exec(text)?.[1] ?? response.status}`);
  return text;
}

function credentials(env: Env): Credentials {
  return { accessKeyId: env.R2_ACCESS_KEY_ID, secretAccessKey: env.R2_SECRET_ACCESS_KEY, region: 'auto', service: 's3' };
}

function r2Host(env: Env) {
  return `${env.R2_ACCOUNT_ID}.r2.cloudflarestorage.com`;
}

function objectPath(env: Env, key: string) {
  return `/${env.BUCKET_NAME}/${key}`;
}

function maxObjectBytes(env: Env) {
  const value = Number(env.MAX_OBJECT_BYTES);
  return Number.isFinite(value) && value > 0 ? value : 10 * 1024 ** 3;
}

function checkUploadId(uploadId: unknown): string {
  if (typeof uploadId !== 'string' || uploadId.length === 0 || uploadId.length > 1024) throw new HttpError(400, 'Invalid uploadId.');
  return uploadId;
}

async function readBody(request: Request): Promise<Body> {
  const length = Number(request.headers.get('content-length') ?? 0);
  if (length > MAX_BODY_BYTES) throw new HttpError(413, 'Request is too large.');
  const text = await request.text();
  if (text.length > MAX_BODY_BYTES) throw new HttpError(413, 'Request is too large.');
  try {
    const body = JSON.parse(text);
    if (body && typeof body === 'object' && !Array.isArray(body)) return body as Body;
  } catch {
    // Fall through
  }
  throw new HttpError(400, 'Expected a JSON object.');
}

function escapeXml(value: string) {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');
}

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: { 'content-type': 'application/json' } });
}
