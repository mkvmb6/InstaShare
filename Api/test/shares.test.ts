import { describe, expect, it } from 'vitest';
import {
  HttpError, checkExpiry, checkHeader, checkKey, checkPrefix, checkSize,
  markerKey, newPrefix, prefixFromMarker, shareToken, verifyToken,
} from '../src/shares';

const uuid = 'bcbc25b7-7545-480f-ab41-512330032e34';

describe('share prefixes', () => {
  it('creates folder and file prefixes the app already uses', () => {
    expect(newPrefix('folder', undefined, uuid)).toBe(`${uuid}/`);
    expect(newPrefix('file', 'Q3 report#1.pdf', uuid)).toBe(`Q3 report#1.pdf-${uuid}/`);
  });

  it('rejects names that would escape the share', () => {
    for (const name of ['a/b', '..\\x', '', '   ', 'x'.repeat(256), 'bad\nname'])
      expect(() => newPrefix('file', name, uuid)).toThrow(HttpError);
  });

  it('only accepts prefixes it could have issued', () => {
    expect(checkPrefix(`${uuid}/`)).toBe(`${uuid}/`);
    expect(checkPrefix(`a-b-${uuid}/`)).toBe(`a-b-${uuid}/`);
    for (const bad of ['', '/', `${uuid}`, `${uuid}/x/`, '_shares/', `../${uuid}/`, 'photos/'])
      expect(() => checkPrefix(bad)).toThrow(HttpError);
  });

  it('round-trips marker keys', () => {
    expect(markerKey(`${uuid}/`)).toBe(`_shares/${uuid}`);
    expect(prefixFromMarker(`_shares/${uuid}`)).toBe(`${uuid}/`);
  });
});

describe('keys', () => {
  const prefix = `${uuid}/`;

  it('allows nested paths inside the share', () => {
    expect(checkKey(prefix, `${prefix}Photos/2024/a #1.jpg`)).toBe(`${prefix}Photos/2024/a #1.jpg`);
  });

  it('blocks anything outside or odd', () => {
    for (const key of [prefix, 'other/x', `${prefix}../x`, `${prefix}a//b`, `${prefix}./a`, `${prefix}a\u0000`, 42])
      expect(() => checkKey(prefix, key)).toThrow(HttpError);
  });
});

describe('limits', () => {
  it('only allows the app expiry choices', () => {
    for (const ok of [1, 6, 12, 24, 48, 168]) expect(checkExpiry(ok)).toBe(ok);
    for (const bad of [0, 2, 10_000, '48', null]) expect(() => checkExpiry(bad)).toThrow(HttpError);
  });

  it('enforces the size limit', () => {
    expect(checkSize(0, 10)).toBe(0);
    expect(() => checkSize(11, 10)).toThrow(expect.objectContaining({ status: 413 }));
    for (const bad of [-1, 1.5, '1', NaN]) expect(() => checkSize(bad, 10)).toThrow(HttpError);
  });

  it('refuses header values that could inject headers', () => {
    expect(checkHeader('attachment; filename="a.txt"', 'cd', false)).toBe('attachment; filename="a.txt"');
    expect(checkHeader(undefined, 'cd', false)).toBeUndefined();
    expect(() => checkHeader(undefined, 'ct', true)).toThrow(HttpError);
    expect(() => checkHeader('a\r\nx-amz-acl: private', 'cd', false)).toThrow(HttpError);
  });
});

describe('share tokens', () => {
  it('accepts the token for its own share only', async () => {
    const token = await shareToken('secret', `${uuid}/`);
    await expect(verifyToken('secret', `${uuid}/`, token)).resolves.toBeUndefined();
    await expect(verifyToken('secret', `x-${uuid}/`, token)).rejects.toMatchObject({ status: 403 });
    await expect(verifyToken('other-secret', `${uuid}/`, token)).rejects.toMatchObject({ status: 403 });
    await expect(verifyToken('secret', `${uuid}/`, undefined)).rejects.toMatchObject({ status: 403 });
  });

  it('is URL-safe', async () => {
    expect(await shareToken('secret', `${uuid}/`)).toMatch(/^[A-Za-z0-9_-]+$/);
  });
});
