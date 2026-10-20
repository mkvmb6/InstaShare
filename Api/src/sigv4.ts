// AWS Signature Version 4 for R2's S3 API, using only Web Crypto.
// aws4fetch would do most of this, but it refuses to sign Content-Length, and signing it is what
// stops a client from uploading more than it asked for.

const encoder = new TextEncoder();

export interface Credentials {
  accessKeyId: string;
  secretAccessKey: string;
  region: string;
  service: string;
}

export interface RequestToSign {
  method: string;
  host: string;
  /** Unencoded path, e.g. "/bucket/My Folder/a#1.txt". */
  path: string;
  query?: Record<string, string>;
  /** Headers to sign. The client must send exactly these values. */
  headers?: Record<string, string>;
  now: Date;
}

/** Percent-encodes as SigV4 requires: everything except A-Z a-z 0-9 - . _ ~ (and "/" in paths). */
export function uriEncode(value: string, encodeSlash = true): string {
  let out = '';
  for (const byte of encoder.encode(value)) {
    const isUnreserved =
      (byte >= 0x41 && byte <= 0x5a) || (byte >= 0x61 && byte <= 0x7a) || (byte >= 0x30 && byte <= 0x39) ||
      byte === 0x2d || byte === 0x2e || byte === 0x5f || byte === 0x7e;
    if (isUnreserved || (byte === 0x2f && !encodeSlash)) out += String.fromCharCode(byte);
    else out += '%' + byte.toString(16).toUpperCase().padStart(2, '0');
  }
  return out;
}

/** Returns a URL that allows exactly this request until it expires. */
export async function presignUrl(credentials: Credentials, request: RequestToSign, expiresSeconds: number): Promise<string> {
  const { amzDate, dateStamp } = timestamps(request.now);
  const scope = credentialScope(credentials, dateStamp);
  const headers = canonicalHeaderMap(request);
  const query: Record<string, string> = {
    ...request.query,
    'X-Amz-Algorithm': 'AWS4-HMAC-SHA256',
    'X-Amz-Credential': `${credentials.accessKeyId}/${scope}`,
    'X-Amz-Date': amzDate,
    'X-Amz-Expires': String(expiresSeconds),
    'X-Amz-SignedHeaders': signedHeaderNames(headers),
  };
  const signature = await sign(credentials, dateStamp, amzDate, scope, canonicalRequest(request.method, request.path, query, headers, 'UNSIGNED-PAYLOAD'));
  return `https://${request.host}${uriEncode(request.path, false)}?${canonicalQuery(query)}&X-Amz-Signature=${signature}`;
}

/** Signs a request with an Authorization header, for calls the Worker makes itself. */
export async function signHeaders(credentials: Credentials, request: RequestToSign, body = ''): Promise<{ url: string; headers: Record<string, string> }> {
  const { amzDate, dateStamp } = timestamps(request.now);
  const scope = credentialScope(credentials, dateStamp);
  const payloadHash = await sha256Hex(body);
  const headers = canonicalHeaderMap({
    ...request,
    headers: { ...request.headers, 'x-amz-content-sha256': payloadHash, 'x-amz-date': amzDate },
  });
  const query = request.query ?? {};
  const signature = await sign(credentials, dateStamp, amzDate, scope, canonicalRequest(request.method, request.path, query, headers, payloadHash));

  const sent = { ...headers };
  delete sent.host; // fetch sets it from the URL
  sent.authorization = `AWS4-HMAC-SHA256 Credential=${credentials.accessKeyId}/${scope}, SignedHeaders=${signedHeaderNames(headers)}, Signature=${signature}`;
  const queryString = canonicalQuery(query);
  return { url: `https://${request.host}${uriEncode(request.path, false)}${queryString ? '?' + queryString : ''}`, headers: sent };
}

function timestamps(now: Date) {
  const amzDate = now.toISOString().replace(/[:-]|\.\d{3}/g, ''); // 20130524T000000Z
  return { amzDate, dateStamp: amzDate.slice(0, 8) };
}

function credentialScope(credentials: Credentials, dateStamp: string) {
  return `${dateStamp}/${credentials.region}/${credentials.service}/aws4_request`;
}

function canonicalHeaderMap(request: RequestToSign): Record<string, string> {
  const headers: Record<string, string> = { host: request.host };
  for (const [name, value] of Object.entries(request.headers ?? {}))
    headers[name.toLowerCase()] = value.trim().replace(/\s+/g, ' ');
  return headers;
}

function signedHeaderNames(headers: Record<string, string>) {
  return Object.keys(headers).sort().join(';');
}

function canonicalQuery(query: Record<string, string>) {
  return Object.entries(query)
    .map(([k, v]) => [uriEncode(k), uriEncode(v)])
    .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
    .map(([k, v]) => `${k}=${v}`)
    .join('&');
}

function canonicalRequest(method: string, path: string, query: Record<string, string>, headers: Record<string, string>, payloadHash: string) {
  const names = Object.keys(headers).sort();
  return [
    method,
    uriEncode(path, false),
    canonicalQuery(query),
    names.map((n) => `${n}:${headers[n]}\n`).join(''),
    names.join(';'),
    payloadHash,
  ].join('\n');
}

async function sign(credentials: Credentials, dateStamp: string, amzDate: string, scope: string, canonical: string) {
  const stringToSign = ['AWS4-HMAC-SHA256', amzDate, scope, await sha256Hex(canonical)].join('\n');
  const kDate = await hmac(encoder.encode('AWS4' + credentials.secretAccessKey), dateStamp);
  const kRegion = await hmac(kDate, credentials.region);
  const kService = await hmac(kRegion, credentials.service);
  const kSigning = await hmac(kService, 'aws4_request');
  return toHex(await hmac(kSigning, stringToSign));
}

export async function hmac(key: ArrayBuffer | Uint8Array, data: string): Promise<ArrayBuffer> {
  const cryptoKey = await crypto.subtle.importKey('raw', key, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  return crypto.subtle.sign('HMAC', cryptoKey, encoder.encode(data));
}

async function sha256Hex(data: string) {
  return toHex(await crypto.subtle.digest('SHA-256', encoder.encode(data)));
}

function toHex(buffer: ArrayBuffer) {
  return [...new Uint8Array(buffer)].map((b) => b.toString(16).padStart(2, '0')).join('');
}
