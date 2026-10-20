// Share IDs, tokens and input checks. Nothing here talks to R2, so it's all unit-testable.

import { hmac } from './sigv4';

/** Expiry choices the desktop app offers. Anything else is rejected, so nobody can keep files forever. */
export const ALLOWED_EXPIRY_HOURS = [1, 6, 12, 24, 48, 168];

/** Single PUT limit of the S3 API. Bigger files must go up in parts. */
export const MAX_SINGLE_PUT_BYTES = 5 * 1024 ** 3;
export const MAX_PART_NUMBER = 10_000;
/** Shares made before the Worker existed always expired after 2 days. */
export const LEGACY_EXPIRY_MS = 2 * 24 * 3600_000;
export const MAX_BATCH = 100;

export const MARKER_PREFIX = '_shares/';

const UUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
// "{uuid}/" for folders, "{file name}-{uuid}/" for single files.
export const SHARE_PREFIX = new RegExp(`^(?:[^/]+-)?${UUID}/$`);

export class HttpError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export function newPrefix(kind: unknown, name: unknown, uuid: string): string {
  if (kind === 'folder') return `${uuid}/`;
  if (kind !== 'file') throw new HttpError(400, 'kind must be "file" or "folder".');
  return `${checkName(name)}-${uuid}/`;
}

export function checkPrefix(prefix: unknown): string {
  if (typeof prefix !== 'string' || !SHARE_PREFIX.test(prefix)) throw new HttpError(400, 'Invalid share.');
  return prefix;
}

/** The marker object that records a share's expiry, so the cron job can find and delete it. */
export function markerKey(prefix: string) {
  return MARKER_PREFIX + prefix.slice(0, -1);
}

export function prefixFromMarker(key: string) {
  return key.slice(MARKER_PREFIX.length) + '/';
}

export function checkExpiry(hours: unknown): number {
  if (typeof hours !== 'number' || !ALLOWED_EXPIRY_HOURS.includes(hours))
    throw new HttpError(400, `expiresInHours must be one of ${ALLOWED_EXPIRY_HOURS.join(', ')}.`);
  return hours;
}

/** An object key inside the share. Blocks "..", empty segments and control characters. */
export function checkKey(prefix: string, key: unknown): string {
  if (typeof key !== 'string' || !key.startsWith(prefix) || key.length === prefix.length)
    throw new HttpError(400, 'Key is outside the share.');
  if (new TextEncoder().encode(key).length > 1024) throw new HttpError(400, 'Key is too long.');
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f]/.test(key)) throw new HttpError(400, 'Key contains control characters.');
  const segments = key.slice(prefix.length).split('/');
  if (segments.some((s) => s === '' || s === '.' || s === '..')) throw new HttpError(400, 'Key has an invalid path segment.');
  return key;
}

export function checkSize(size: unknown, max: number): number {
  if (typeof size !== 'number' || !Number.isSafeInteger(size) || size < 0) throw new HttpError(400, 'size must be a whole number of bytes.');
  if (size > max) throw new HttpError(413, `File is larger than the ${formatBytes(max)} limit.`);
  return size;
}

export function checkPartNumber(partNumber: unknown): number {
  if (typeof partNumber !== 'number' || !Number.isInteger(partNumber) || partNumber < 1 || partNumber > MAX_PART_NUMBER)
    throw new HttpError(400, `partNumber must be 1-${MAX_PART_NUMBER}.`);
  return partNumber;
}

/** A header value the client will send back verbatim: short, and no line breaks to smuggle extra headers. */
export function checkHeader(value: unknown, name: string, required: boolean): string | undefined {
  if (value === undefined || value === null) {
    if (required) throw new HttpError(400, `${name} is required.`);
    return undefined;
  }
  if (typeof value !== 'string' || value.length > 2000 || /[\r\n]/.test(value)) throw new HttpError(400, `Invalid ${name}.`);
  return value;
}

export function checkBatch<T>(items: unknown, name: string): T[] {
  if (!Array.isArray(items) || items.length === 0 || items.length > MAX_BATCH)
    throw new HttpError(400, `${name} must have 1-${MAX_BATCH} entries.`);
  return items as T[];
}

/**
 * Proves the caller created the share. It's an HMAC of the prefix, so the Worker needs no database:
 * whoever holds the token can upload into and delete that one share, and nothing else.
 */
export async function shareToken(secret: string, prefix: string): Promise<string> {
  const mac = await hmac(new TextEncoder().encode(secret), `share:${prefix}`);
  return btoa(String.fromCharCode(...new Uint8Array(mac))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export async function verifyToken(secret: string, prefix: string, token: unknown): Promise<void> {
  const expected = await shareToken(secret, prefix);
  if (typeof token !== 'string' || !constantTimeEquals(expected, token)) throw new HttpError(403, 'Not allowed for this share.');
}

function constantTimeEquals(a: string, b: string) {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

function checkName(name: unknown): string {
  if (typeof name !== 'string') throw new HttpError(400, 'name is required for file shares.');
  const trimmed = name.trim();
  // eslint-disable-next-line no-control-regex
  if (trimmed.length === 0 || trimmed.length > 255 || /[/\\\u0000-\u001f\u007f]/.test(trimmed))
    throw new HttpError(400, 'Invalid file name.');
  return trimmed;
}

function formatBytes(bytes: number) {
  return bytes >= 1024 ** 3 ? `${(bytes / 1024 ** 3).toFixed(0)} GB` : `${(bytes / 1024 ** 2).toFixed(0)} MB`;
}
