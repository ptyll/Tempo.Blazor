#!/usr/bin/env node
// Strict CSS token audit (00-cross-cutting §7.3). The lenient audit
// (audit-css-tokens.mjs) ignores a var(--tm-*) that carries a fallback and ignores colour
// literals entirely, which is how undefined aliases with hex fallbacks ship. This one reports:
//
//   undefined-token  — a var(--tm-*) whose token is defined nowhere, fallback or not
//   color-literal    — a hex / rgb() / rgba() / hsl() / hsla() outside the allowlist
//   white-on-primary — color: #fff / white / var(--tm-color-white) on a primary fill
//   component-dark   — a [data-theme="dark"] (or .tm-dark) block inside a component stylesheet
//
// The allowlist is the token and theme files: literals there are definitions, not drift.
// CLI: node scripts/audit-css-strict.mjs [--update-baseline]
import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { collectCssFiles, collectRuntimeSourceFiles, findRepoRoot, parseDefinitions, parseUsages, stripCssComments } from './audit-css-tokens.mjs';

const BASELINE_FILE = 'scripts/css-token-baseline.json';

const COLOR_LITERAL = /#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|(?<![\w-])(?:white|black)(?![\w-])/g;
const NAMED_COLOUR = /^(?:white|black)$/i;
const WHITE_COLOR = /(?<![\w-])(?:#fff|#ffffff|white)(?![\w-])|var\(\s*--tm-color-white\s*\)/i;
const COMPONENT_DARK = /\[data-theme\s*=\s*["']dark["']\]|\.tm-dark\b/g;
// A primary fill is a solid primary step. A wash (color-mix, -subtle, -50) is not white-on-primary.
const PRIMARY_FILL = /var\(\s*--tm-color-primary(?:-hover|-active|-[5-9]00)?\s*[,)]/i;

/**
 * A custom-property definition that is a data palette entry — a literal there is a data
 * value, not drift. Matches `--tm-<name>-(palette|option|role|annotation|category)-<name>:`.
 */
// A data palette entry may hold a literal. option/role entries are palettes too, but a state or
// part suffix (-hover, -bg, …) is a component token wearing a palette-shaped name, not data.
const DATA_PALETTE_DEFINITION = /^\s*--tm-[a-z0-9-]+-(palette|option|role|annotation|category)-[a-z0-9-]+(?<!-(?:hover|active|selected|focus|disabled|bg|fg|text|border))\s*:/i;

/** The declarations of a rule body, property lower-cased and trimmed. */
function declarations(body) {
  return body.split(';').flatMap(part => {
    const separator = part.indexOf(':');
    if (separator < 0) {
      return [];
    }
    return [{ property: part.slice(0, separator).trim().toLowerCase(), value: part.slice(separator + 1) }];
  });
}

/**
 * Colour literals on a line that count as drift. A named colour inside an attribute selector or a
 * quoted string is a selector, not a colour — `rect[fill="white"]` is not a literal.
 */
function colourLiterals(line) {
  return [...line.matchAll(COLOR_LITERAL)].filter(match =>
    !(NAMED_COLOUR.test(match[0]) && insideSelectorOrQuote(line, match.index)));
}

function insideSelectorOrQuote(line, index) {
  let brackets = 0;
  let quote = null;
  for (let i = 0; i < index; i++) {
    const ch = line[i];
    if (quote) {
      if (ch === quote && line[i - 1] !== '\\') {
        quote = null;
      }
      continue;
    }
    if (ch === '"' || ch === "'") {
      quote = ch;
      continue;
    }
    if (ch === '[') {
      brackets++;
    } else if (ch === ']') {
      brackets = Math.max(0, brackets - 1);
    }
  }
  return brackets > 0 || quote !== null;
}

/**
 * Tokens a .cs/.razor/.js file actually sets at runtime. Only a setProperty('--tm-x') call or a
 * `--tm-x:` declaration inside a string literal counts — a mention in a comment or a doc is not a
 * definition, and counting it hid undefined tokens.
 */
export function runtimeTokenDefinitions(source) {
  const stripped = source
    .replace(/\/\*[\s\S]*?\*\//g, match => match.replace(/[^\n]/g, ' '))
    .replace(/@\*[\s\S]*?\*@/g, match => match.replace(/[^\n]/g, ' '))
    .replace(/(^|[^:"'`])\/\/[^\n]*/g, '$1');

  const definitions = new Set();
  for (const match of stripped.matchAll(/setProperty\(\s*['"](--tm-[a-zA-Z0-9-]+)['"]/g)) {
    definitions.add(match[1]);
  }

  let quote = null;
  for (let i = 0; i < stripped.length; i++) {
    const ch = stripped[i];
    if (quote) {
      if (ch === '\\') {
        i++;
        continue;
      }
      if (ch === quote) {
        quote = null;
        continue;
      }
      if (ch === '-' && stripped[i + 1] === '-') {
        const token = /^--tm-[a-zA-Z0-9-]+/.exec(stripped.slice(i));
        if (token && stripped[i + token[0].length] === ':') {
          definitions.add(token[0]);
        }
      }
      continue;
    }
    if (ch === '"' || ch === "'" || ch === '`') {
      quote = ch;
    }
  }
  return definitions;
}

/** A token or theme stylesheet is allowed to contain colour literals — they are definitions. */
export function isAllowlisted(relativePath) {
  const name = path.basename(relativePath);
  return name.startsWith('tokens') || name.startsWith('theme-');
}

/**
 * Audit an in-memory stylesheet map. Keys are paths relative to the css root; a token is
 * "defined" when any file in the map declares it. Used by the fixture tests so the contract
 * does not depend on the repository happening to be clean.
 */
export function auditCssStrict(files) {
  const definitions = new Set();
  for (const css of Object.values(files)) {
    for (const definition of parseDefinitions(css)) {
      definitions.add(definition);
    }
  }

  const violations = [];
  for (const [file, css] of Object.entries(files)) {
    const stripped = stripCssComments(css);
    for (const usage of parseUsages(stripped)) {
      if (!definitions.has(usage.token)) {
        violations.push({ kind: 'undefined-token', token: usage.token, file, line: usage.line });
      }
    }

    if (isAllowlisted(file)) {
      continue;
    }

    const lines = stripped.split('\n');
    lines.forEach((line, index) => {
      if (colourLiterals(line).length > 0 && !DATA_PALETTE_DEFINITION.test(line)) {
        violations.push({ kind: 'color-literal', file, line: index + 1, text: line.trim() });
      }
      if (COMPONENT_DARK.test(line)) {
        violations.push({ kind: 'component-dark', file, line: index + 1, text: line.trim() });
      }
      COMPONENT_DARK.lastIndex = 0;
    });

    // White on a primary fill is judged per declaration, not per block of text: `color` must be
    // the property itself (white-space or border-color do not qualify) and the fill must be a
    // solid primary step (a wash does not).
    for (const match of stripped.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
      const declared = declarations(match[2]);
      const paintsPrimary = declared.some(declaration =>
        /^background(-color)?$/.test(declaration.property) && PRIMARY_FILL.test(declaration.value));
      const paintsWhite = declared.some(declaration =>
        declaration.property === 'color' && WHITE_COLOR.test(declaration.value));
      if (paintsPrimary && paintsWhite) {
        const line = stripped.slice(0, match.index).split('\n').length;
        violations.push({ kind: 'white-on-primary', file, line, text: match[1].trim() });
      }
    }
  }

  return violations;
}

/** The repository audit: every stylesheet under src/, keyed by its path relative to the repo. */
export function auditRepository(repoRoot) {
  const files = {};
  for (const file of collectCssFiles(repoRoot)) {
    // The bundle inlines every stylesheet, so counting it would report each violation twice and
    // move the baseline whenever the bundle is regenerated. The sources are what matters.
    if (file.endsWith('.bundled.css')) {
      continue;
    }
    files[path.relative(repoRoot, file).split(path.sep).join('/')] = readFileSync(file, 'utf8');
  }

  // Tokens set from script at runtime are defined, just not in a stylesheet. Their source is not
  // a stylesheet, so only the definitions count — scanning it for colour literals would report
  // every hex in a .js or .cs file.
  const runtimeDefinitions = new Set();
  for (const file of collectRuntimeSourceFiles(repoRoot)) {
    for (const definition of runtimeTokenDefinitions(readFileSync(file, 'utf8'))) {
      runtimeDefinitions.add(definition);
    }
  }

  return auditCssStrict(files).filter(
    violation => violation.kind !== 'undefined-token' || !runtimeDefinitions.has(violation.token));
}

/**
 * Line-independent identity. A baseline keyed on line numbers fails every time an unrelated edit
 * shifts a line, so the key is the violation itself and the baseline counts occurrences. Adding one
 * more still fails; reformatting does not.
 */
export function violationKey(violation) {
  const text = (violation.token ?? violation.text ?? '').replace(/\s+/g, ' ').trim();
  return `${violation.kind}|${violation.file}|${text}`;
}

export function countByKey(violations) {
  const counts = {};
  for (const violation of violations) {
    const key = violationKey(violation);
    counts[key] = (counts[key] ?? 0) + 1;
  }
  return counts;
}

const isMain = process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isMain) {
  const repoRoot = findRepoRoot(path.dirname(fileURLToPath(import.meta.url)));
  const counts = countByKey(auditRepository(repoRoot));
  const total = Object.values(counts).reduce((sum, count) => sum + count, 0);
  const baselinePath = path.join(repoRoot, BASELINE_FILE);

  if (process.argv.includes('--update-baseline')) {
    const ordered = Object.fromEntries(Object.entries(counts).sort(([a], [b]) => a.localeCompare(b)));
    writeFileSync(baselinePath, JSON.stringify({ count: total, violations: ordered }, null, 2) + '\n');
    console.log(`audit-css-strict: wrote ${total} violation(s) to ${BASELINE_FILE}.`);
    process.exit(0);
  }

  const baseline = JSON.parse(readFileSync(baselinePath, 'utf8'));
  const regressions = Object.entries(counts)
    .filter(([key, count]) => count > (baseline.violations[key] ?? 0))
    .map(([key, count]) => `${key} (${baseline.violations[key] ?? 0} -> ${count})`);
  console.log(`audit-css-strict: ${total} violation(s), baseline ${baseline.count}.`);
  if (regressions.length > 0) {
    console.error(`audit-css-strict: ${regressions.length} violation(s) grew — the baseline may only shrink:`);
    for (const regression of regressions) {
      console.error(`  ${regression}`);
    }
    process.exit(1);
  }
  if (total < baseline.count) {
    console.error('audit-css-strict: the count dropped. Re-run with --update-baseline so the baseline shrinks.');
    process.exit(1);
  }
  console.log('audit-css-strict: no new violations.');
}
