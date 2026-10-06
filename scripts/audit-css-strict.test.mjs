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

test('on-primary text with white-space is not white-on-primary', () => {
  const violations = auditCssStrict({
    'components/badge.css': '.badge {\n  background: var(--tm-color-primary);\n  color: var(--tm-color-on-primary);\n  white-space: nowrap;\n}',
    'tokens.css': ':root { --tm-color-primary: #2563eb; --tm-color-on-primary: #fff; }'
  });

  assert.deepEqual(
    violations.filter(violation => violation.kind === 'white-on-primary'),
    [],
    'white-space and on-primary ink are not white text on a primary fill');
});

test('a white border on a primary fill is not white-on-primary', () => {
  const violations = auditCssStrict({
    'components/badge.css': '.badge {\n  background-color: var(--tm-color-primary);\n  border-color: white;\n}',
    'tokens.css': ':root { --tm-color-primary: #2563eb; }'
  });

  assert.deepEqual(
    violations.filter(violation => violation.kind === 'white-on-primary'),
    [],
    'only the color property counts, not a border');
});

test('a primary wash is not white-on-primary', () => {
  const violations = auditCssStrict({
    'components/badge.css': '.badge {\n  background: var(--tm-color-primary-subtle);\n  color: #fff;\n}',
    'tokens.css': ':root { --tm-color-primary-subtle: #eff6ff; }'
  });

  assert.deepEqual(
    violations.filter(violation => violation.kind === 'white-on-primary'),
    [],
    'a wash is not a solid primary fill');
});

test('a palette-shaped token with a state suffix is still a colour literal', () => {
  const violations = auditCssStrict({
    'components/dropdown.css': '.menu {\n  --tm-dropdown-option-hover-bg: #fff;\n}',
    'tokens.css': ':root { --tm-color-primary: #2563eb; }'
  });

  assert.ok(
    violations.some(violation => violation.kind === 'color-literal'),
    `a state suffix is a component token, not a palette entry, got: ${JSON.stringify(violations)}`);
});

test('a named colour inside an attribute selector is not a colour literal', () => {
  const violations = auditCssStrict({
    'components/chart.css': '.chart {\n  background: var(--tm-color-primary);\n}\nrect[fill="white"] { opacity: 0.5; }',
    'tokens.css': ':root { --tm-color-primary: #2563eb; }'
  });

  assert.deepEqual(
    violations.filter(violation => violation.kind === 'color-literal'),
    [],
    'fill="white" is a selector, not a painted colour');
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
