#!/usr/bin/env node
// Summarises the shard TRX files of the full E2E lane into summary.json + summary.md (per class, per shard).
// Regex-based on purpose: no extra dependency, and the TRX schema shape this reads is stable.
import fs from 'node:fs';
import path from 'node:path';

const dir = process.argv[2];
if (!dir) {
  console.error('usage: e2e-summary.mjs <report-dir>');
  process.exit(2);
}

const shardRuns = [];
for (const file of fs.readdirSync(dir).filter((f) => f.endsWith('.trx')).sort()) {
  const xml = fs.readFileSync(path.join(dir, file), 'utf8');
  // "shard-3.trx" -> 3 (the class shard index); any other name is kept as its own label.
  const shard = /^shard-(\d+)\.trx$/.test(file) ? Number(/^shard-(\d+)\.trx$/.exec(file)[1]) : file.replace(/\.trx$/, '');

  const classOf = new Map();
  for (const m of xml.matchAll(/<UnitTest\b[^>]*\bid="([^"]+)"[\s\S]*?<TestMethod\b[^>]*className="([^"]+)"/g)) {
    classOf.set(m[1], m[2]);
  }

  const classes = new Map();
  let passed = 0, failed = 0, skipped = 0;
  const failures = [];
  for (const m of xml.matchAll(/<UnitTestResult\b([^>]*)>/g)) {
    const attrs = m[1];
    const outcome = /outcome="([^"]+)"/.exec(attrs)?.[1] ?? 'Unknown';
    const testId = /testId="([^"]+)"/.exec(attrs)?.[1] ?? '';
    const testName = /testName="([^"]+)"/.exec(attrs)?.[1] ?? '';
    const duration = /duration="([^"]+)"/.exec(attrs)?.[1] ?? '';
    const seconds = (() => {
      const t = /^PT(?:(\d+)H)?(?:(\d+)M)?(?:([\d.]+)S)?$/.exec(duration);
      if (!t) return 0;
      return Number(t[1] ?? 0) * 3600 + Number(t[2] ?? 0) * 60 + Number(t[3] ?? 0);
    })();
    const className = classOf.get(testId) ?? '(unknown)';
    const short = className.split('.').pop();
    const entry = classes.get(short) ?? { passed: 0, failed: 0, skipped: 0, seconds: 0, failures: [] };
    if (outcome === 'Passed') { entry.passed += 1; passed += 1; }
    else if (outcome === 'Failed') { entry.failed += 1; failed += 1; entry.failures.push(testName); failures.push(`${className}.${testName}`); }
    else { entry.skipped += 1; skipped += 1; }
    entry.seconds += seconds;
    classes.set(short, entry);
  }

  const outcome = /<ResultSummary\b[^>]*outcome="([^"]+)"/.exec(xml)?.[1] ?? '';
  shardRuns.push({ shard, file, outcome, passed, failed, skipped, total: passed + failed + skipped, classes: Object.fromEntries(classes), failures });
}

const shardMeta = fs.existsSync(path.join(dir, 'shards.jsonl'))
  ? fs.readFileSync(path.join(dir, 'shards.jsonl'), 'utf8').trim().split('\n').filter(Boolean).map((l) => JSON.parse(l))
  : [];

const totals = shardRuns.reduce(
  (a, s) => ({ passed: a.passed + s.passed, failed: a.failed + s.failed, skipped: a.skipped + s.skipped, total: a.total + s.total }),
  { passed: 0, failed: 0, skipped: 0, total: 0 });

const perClass = new Map();
for (const s of shardRuns) {
  for (const [name, e] of Object.entries(s.classes)) {
    const row = perClass.get(name) ?? { passed: 0, failed: 0, skipped: 0, seconds: 0, shard: s.shard, failures: [] };
    row.passed += e.passed; row.failed += e.failed; row.skipped += e.skipped; row.seconds += e.seconds;
    row.failures.push(...e.failures);
    perClass.set(name, row);
  }
}

const rows = [...perClass.entries()].sort((a, b) => a[0].localeCompare(b[0]));
const md = [];
md.push('# Full E2E lane — per class');
md.push('');
md.push(`Totals: **${totals.passed} passed / ${totals.failed} failed / ${totals.skipped} skipped** (${totals.total} results) over ${shardRuns.length} shard(s).`);
md.push('');
md.push('| Shard | Classes | Results | Passed | Failed | Skipped | Outcome | Wall clock |');
md.push('|---|---|---|---|---|---|---|---|');
for (const s of shardRuns) {
  const meta = shardMeta.find((m) => m.shard === s.shard);
  md.push(`| ${s.shard} | ${Object.keys(s.classes).length} | ${s.total} | ${s.passed} | ${s.failed} | ${s.skipped} | ${s.outcome} | ${meta ? Math.round(meta.seconds / 60) + ' min' : '?'} |`);
}
md.push('');
md.push('| Class | Results | Passed | Failed | Skipped | Test time |');
md.push('|---|---|---|---|---|---|');
for (const [name, r] of rows) {
  md.push(`| \`${name}\` | ${r.passed + r.failed + r.skipped} | ${r.passed} | ${r.failed} | ${r.skipped} | ${(r.seconds / 60).toFixed(1)} min |`);
}
if (totals.failed > 0) {
  md.push('');
  md.push('## Failures');
  md.push('');
  for (const s of shardRuns) for (const f of s.failures) md.push(`- shard ${s.shard}: \`${f}\``);
}

fs.writeFileSync(path.join(dir, 'summary.md'), md.join('\n') + '\n');
fs.writeFileSync(path.join(dir, 'summary.json'), JSON.stringify({ totals, shards: shardRuns, perClass: Object.fromEntries(rows) }, null, 2));
console.log(`summary.md + summary.json written: ${totals.passed} passed / ${totals.failed} failed / ${totals.skipped} skipped`);
