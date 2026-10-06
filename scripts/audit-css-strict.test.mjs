import test from 'node:test';
import assert from 'node:assert/strict';
import { auditCssStrict } from './audit-css-strict.mjs';

// RED contract for the strict audit (00-cross-cutting §7.3). The lenient audit ignores a
// var(--tm-*) that carries a fallback and ignores colour literals entirely, which is exactly
// why undefined aliases with hex fallbacks ship. Both shapes must fail here, on a fixture,
// before the audit is allowed to gate the repository.

const undefinedAliasWithFallback = `
.panel {
  border: 1px solid var(--tm-not-a-token, #cccccc);
}
`;

const hexLiteral = `
.panel {
  background: #ff00aa;
}
`;

test('strict audit fails on an undefined --tm-* alias even when it has a fallback', () => {
  const violations = auditCssStrict({
    'components/panel.css': undefinedAliasWithFallback,
    'tokens.css': ':root { --tm-color-primary: #2563eb; }'
  });

  assert.ok(
    violations.some(violation => violation.kind === 'undefined-token' && violation.token === '--tm-not-a-token'),
    `an undefined alias with a fallback must be a violation, got: ${JSON.stringify(violations)}`);
});

test('strict audit fails on a hex literal outside the allowlist', () => {
  const violations = auditCssStrict({
    'components/panel.css': hexLiteral,
    'tokens.css': ':root { --tm-color-primary: #2563eb; }'
  });

  assert.ok(
    violations.some(violation => violation.kind === 'color-literal'),
    `a hex literal in a component stylesheet must be a violation, got: ${JSON.stringify(violations)}`);
});

test('strict audit allows hex literals inside token and theme stylesheets', () => {
  const violations = auditCssStrict({
    'tokens.css': ':root { --tm-color-primary: #2563eb; }',
    'theme-indigo.css': '[data-tm-theme="indigo"] { --tm-color-primary-600: #4f46e5; }'
  });

  assert.deepEqual(
    violations.filter(violation => violation.kind === 'color-literal'),
    [],
    'token and theme files are the allowlist — literals there are definitions, not drift');
});

test('a data palette custom property may hold a literal', () => {
  const violations = auditCssStrict({
    'components/chart.css': '.chart {\n  --tm-chart-palette-revenue: #22c55e;\n  background: var(--tm-color-primary);\n}',
    'tokens.css': ':root { --tm-color-primary: #2563eb; }'
  });

  assert.deepEqual(
    violations.filter(violation => violation.kind === 'color-literal'),
    [],
    'a --tm-*-palette-* definition is data, not drift');
});

test('white text on a primary fill is reported even when the two declarations are on different lines', () => {
  const violations = auditCssStrict({
    'components/badge.css': '.badge {\n  background: var(--tm-color-primary);\n  color: #fff;\n}',
    'tokens.css': ':root { --tm-color-primary: #2563eb; --tm-color-white: #fff; }'
  });

  assert.ok(
    violations.some(violation => violation.kind === 'white-on-primary'),
    `white on a primary fill must be a violation, got: ${JSON.stringify(violations)}`);
});

test('a dark-theme block inside a component stylesheet is reported', () => {
  const violations = auditCssStrict({
    'components/panel.css': '.panel { color: var(--tm-text-primary); }\n[data-theme="dark"] .panel { color: #fff; }',
    'tokens.css': ':root { --tm-text-primary: #0f172a; }'
  });

  assert.ok(
    violations.some(violation => violation.kind === 'component-dark'),
    `a component dark block must be a violation, got: ${JSON.stringify(violations)}`);
});
