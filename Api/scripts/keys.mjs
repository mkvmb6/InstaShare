// Manage InstaShare API keys.
//
//   npm run keys -- create "Mohit"            New key with the default 50 GB/day cap; prints it once
//   npm run keys -- create "Mom" --gb 10      New key with a 10 GB/day cap
//   npm run keys -- list                      Who has a key, and their caps
//   npm run keys -- revoke "Mom"              Remove a key; it stops working at once
//
// KV stores only a hash of each key, so a lost key can't be recovered. Revoke it and create a new one.

import { execFileSync } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const BINDING = 'API_KEYS';
const HASH_PREFIX = 'key:';
const DEFAULT_GB = 50;

const [command, ...rest] = process.argv.slice(2);

function wrangler(args, { input } = {}) {
    // npx resolves the project's pinned wrangler; shell is needed on Windows to find npx.cmd.
    return execFileSync('npx', ['wrangler', ...args], { encoding: 'utf8', input, shell: process.platform === 'win32', stdio: ['pipe', 'pipe', 'inherit'] });
}

const quote = (s) => (process.platform === 'win32' ? `"${s.replace(/"/g, '\\"')}"` : s);

function listRecords() {
    const keys = JSON.parse(wrangler(['kv', 'key', 'list', '--binding', BINDING, '--remote', '--prefix', HASH_PREFIX]));
    return keys.map((k) => {
        const record = JSON.parse(wrangler(['kv', 'key', 'get', quote(k.name), '--binding', BINDING, '--remote', '--text']));
        return { kvKey: k.name, ...record };
    });
}

function putRecord(kvKey, record) {
    // Through a file, so the JSON survives Windows shell quoting.
    const dir = mkdtempSync(join(tmpdir(), 'instashare-key-'));
    const file = join(dir, 'record.json');
    try {
        writeFileSync(file, JSON.stringify(record));
        wrangler(['kv', 'key', 'put', quote(kvKey), '--path', quote(file), '--binding', BINDING, '--remote']);
    } finally {
        rmSync(dir, { recursive: true, force: true });
    }
}

function nameArg() {
    const name = rest.find((a) => !a.startsWith('--'));
    if (!name) throw new Error('Give the key a name, e.g. npm run keys -- create "Mohit"');
    return name;
}

function create() {
    const name = nameArg();
    const gbIndex = rest.indexOf('--gb');
    const gb = gbIndex >= 0 ? Number(rest[gbIndex + 1]) : DEFAULT_GB;
    if (!(gb > 0)) throw new Error('--gb must be a positive number.');
    if (listRecords().some((r) => r.name.toLowerCase() === name.toLowerCase()))
        throw new Error(`A key called "${name}" already exists. Revoke it first, or pick another name.`);

    const key = 'isk_' + randomBytes(32).toString('base64url');
    const hash = createHash('sha256').update(key).digest('hex');
    putRecord(HASH_PREFIX + hash, { name, dailyBytes: Math.round(gb * 1024 ** 3), created: new Date().toISOString() });

    console.log(`\nKey for "${name}" (${gb} GB a day). It is shown only now; paste it into InstaShare → Settings → API key:\n`);
    console.log(`    ${key}\n`);
}

function list() {
    const records = listRecords();
    if (records.length === 0) return console.log('No keys yet. Create one with: npm run keys -- create "Your name"');
    for (const r of records)
        console.log(`${r.name.padEnd(24)} ${(r.dailyBytes / 1024 ** 3).toFixed(0).padStart(4)} GB/day   created ${r.created.slice(0, 10)}`);
}

function revoke() {
    const name = nameArg();
    const matches = listRecords().filter((r) => r.name.toLowerCase() === name.toLowerCase());
    if (matches.length === 0) throw new Error(`No key called "${name}". See: npm run keys -- list`);
    for (const m of matches) wrangler(['kv', 'key', 'delete', quote(m.kvKey), '--binding', BINDING, '--remote']);
    console.log(`Revoked "${name}". KV changes can take up to a minute to reach every Cloudflare location.`);
}

const commands = { create, list, revoke };
try {
    if (Object.hasOwn(commands, command)) commands[command]();
    else console.log('Usage: npm run keys -- create <name> [--gb N] | list | revoke <name>');
} catch (error) {
    console.error(`\n${error.message}`);
    process.exit(1);
}
