import { describe, expect, it } from 'vitest';
import { presignUrl, signHeaders, uriEncode } from '../src/sigv4';

// Example credentials and expected signatures from the AWS S3 SigV4 documentation.
const aws = { accessKeyId: 'AKIAIOSFODNN7EXAMPLE', secretAccessKey: 'wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY', region: 'us-east-1', service: 's3' };
const now = new Date('2013-05-24T00:00:00Z');

describe('sigv4', () => {
  it('matches the AWS presigned URL example', async () => {
    const url = await presignUrl(aws, { method: 'GET', host: 'examplebucket.s3.amazonaws.com', path: '/test.txt', now }, 86400);
    expect(url).toContain('X-Amz-Signature=aeeed9bbccd4d02ee5c0109b86d86835f995330da4c265957d157751f604d404');
    expect(url).toContain('X-Amz-Credential=AKIAIOSFODNN7EXAMPLE%2F20130524%2Fus-east-1%2Fs3%2Faws4_request');
  });

  it('matches the AWS signed GET example', async () => {
    const signed = await signHeaders(aws, { method: 'GET', host: 'examplebucket.s3.amazonaws.com', path: '/test.txt', headers: { range: 'bytes=0-9' }, now });
    expect(signed.headers.authorization).toBe(
      'AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, ' +
        'SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, ' +
        'Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41',
    );
  });

  it('signs Content-Length, so a URL only accepts the size it was issued for', async () => {
    const url = await presignUrl(aws, { method: 'PUT', host: 'h', path: '/b/k', headers: { 'content-length': '42' }, now }, 60);
    expect(new URL(url).searchParams.get('X-Amz-SignedHeaders')).toBe('content-length;host');
  });

  it('encodes keys the way S3 expects', () => {
    expect(uriEncode('/b/My Photos/a#1?.txt', false)).toBe('/b/My%20Photos/a%231%3F.txt');
    expect(uriEncode('é~-._')).toBe('%C3%A9~-._');
    expect(uriEncode('a/b')).toBe('a%2Fb');
  });
});
