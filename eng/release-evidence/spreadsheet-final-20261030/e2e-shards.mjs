#!/usr/bin/env node
// Enumerates the E2E test classes with their [TestMethod] counts and splits them into N balanced shards.
// Mirrors scripts/e2e-lane-common.ps1 (Get-E2ETestClasses + Split-E2EShards) for a machine without pwsh.
import fs from 'node:fs';
import path from 'node:path';

const projectDir = process.argv[2];
const shardCount = Number(process.argv[3] || 8);
const mode = process.argv[4] || 'filters'; // 'filters' | 'classes'

function walk(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'bin' || entry.name === 'obj') continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, out);
    else if (entry.name.endsWith('.cs')) out.push(full);
  }
  return out;
}

const own = new Map();
const baseOf = new Map();
for (const file of walk(projectDir)) {
  const text = fs.readFileSync(file, 'utf8');
  const methodCount = (text.match(/\[(Test|DataTest)Method\b/g) || []).length;
  const declared = [...text.matchAll(/^[ \t]*(?:public|internal|private|protected)?[ \t]*(?:sealed[ \t]+|static[ \t]+|partial[ \t]+|abstract[ \t]+)*class[ \t]+(\w+)(?:<[^>]*>)?[ \t]*(?::[ \t]*([\w.]+))?/gm)];
  const names = [...new Set(declared.map((m) => m[1]))];
  for (const m of declared) {
    if (m[2]) baseOf.set(m[1], m[2].split('.').pop());
  }
  if (methodCount === 0 || names.length === 0) continue;
  const perClass = Math.max(1, Math.ceil(methodCount / names.length));
  for (const name of names) own.set(name, (own.get(name) || 0) + perClass);
}

const allNames = new Set([...own.keys(), ...[...baseOf.keys()].filter((k) => !own.has(k))]);
const classes = [];
for (const name of allNames) {
  let total = 0;
  const seen = new Set();
  for (let c = name; c && !seen.has(c); c = baseOf.get(c)) {
    seen.add(c);
    total += own.get(c) || 0;
  }
  classes.push({ name, tests: total });
}

const shards = Array.from({ length: Math.min(shardCount, classes.length) }, (_, i) => ({ index: i + 1, total: 0, classes: [] }));
for (const c of [...classes].sort((a, b) => b.tests - a.tests || a.name.localeCompare(b.name))) {
  const target = [...shards].sort((a, b) => a.total - b.total || a.index - b.index)[0];
  target.classes.push(c.name);
  target.total += c.tests;
}

if (mode === 'classes') {
  console.log(JSON.stringify(shards, null, 2));
} else {
  for (const shard of shards) {
    const classPart = '(' + shard.classes.map((c) => `FullyQualifiedName~.${c}.`).join('|') + ')';
    console.log(`${shard.index}\t${shard.classes.length}\t${shard.total}\t(TestCategory!=Performance)&${classPart}`);
  }
}
