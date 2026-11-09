#!/usr/bin/env node
// Deterministic Node test runner: enumerates *.test.mjs files explicitly via
// the filesystem instead of relying on `node --test` glob expansion, which
// varies across Node versions and shells (a stale/unsupported glob silently
// runs a subset). Fails loudly when a root is missing or matches no files.
// Each root may be a directory (walked recursively) or an explicit
// *.test.mjs file, so a lane can pin an exact file list without globs.
import { spawnSync } from 'node:child_process';
import { readdirSync, statSync, existsSync } from 'node:fs';
import path from 'node:path';

const roots = process.argv.slice(2);
if (roots.length === 0) {
  console.error('Usage: node scripts/run-node-tests.mjs <dir-or-file> [<dir-or-file> ...]');
  process.exit(2);
}

const files = new Set();
function walk(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(full);
    } else if (entry.isFile() && entry.name.endsWith('.test.mjs')) {
      files.add(full);
    }
  }
}

for (const root of roots) {
  if (!existsSync(root)) {
    console.error(`Test root does not exist: ${root}`);
    process.exit(2);
  }
  if (statSync(root).isDirectory()) {
    walk(root);
  } else if (root.endsWith('.test.mjs')) {
    files.add(root);
  } else {
    console.error(`Test root is neither a directory nor a *.test.mjs file: ${root}`);
    process.exit(2);
  }
}

const list = [...files].sort();
if (list.length === 0) {
  console.error(`No *.test.mjs files found under: ${roots.join(', ')}`);
  process.exit(2);
}

console.log(`run-node-tests: ${list.length} test file(s) under ${roots.join(', ')}`);
const result = spawnSync(process.execPath, ['--test', ...list], { stdio: 'inherit' });
process.exit(result.status ?? 1);
