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
