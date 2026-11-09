// BPMN interop lane (plan 6fca9139-c871-41c4-c06e-08df38c30cbd, phase 4, finding 14):
// every committed export golden must be readable by bpmn-moddle (the reference BPMN 2.0
// JavaScript toolkit, MIT) with zero warnings/errors and a bpmn:Definitions root.
// Run: npm run test:bpmn-interop
import test from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { BpmnModdle } from 'bpmn-moddle';

const here = path.dirname(fileURLToPath(import.meta.url));
const expectedDir = path.join(here, '..', 'BpmnCorpus', 'Expected');

const goldens = readdirSync(expectedDir)
  .filter(name => name.endsWith('.export.bpmn'))
  .sort();

assert.ok(goldens.length > 0, 'no export goldens found');

const moddle = new BpmnModdle();

for (const golden of goldens) {
  test(`bpmn-moddle parses ${golden} with zero warnings`, async () => {
    const xml = readFileSync(path.join(expectedDir, golden), 'utf8');
    const result = await moddle.fromXML(xml);

    assert.equal(result.warnings.length, 0,
      `expected zero warnings, got: ${JSON.stringify(result.warnings, null, 2)}`);
    assert.ok(result.rootElement, 'a root element must be produced');
    assert.equal(result.rootElement.$type, 'bpmn:Definitions',
      `root must be bpmn:Definitions, got ${result.rootElement.$type}`);
  });
}
