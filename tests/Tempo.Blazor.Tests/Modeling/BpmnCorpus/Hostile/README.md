# Hostile BPMN fixtures (phase 2)

Phase 2 of plan `6fca9139-c871-41c4-c06e-08df38c30cbd` adds adversarial security
fixtures here: XXE payloads, billion-laughs entities, DTD probes, oversized/zip-bomb
streams and malformed-namespace documents. They live OUTSIDE `ExpectedCorpusFiles`
in `BpmnCorpusTests` (they must NOT parse cleanly with safe settings) and drive the
mandatory `HasErrors` tests for `MaxInputBytes` / `MaxDepth` / `MaxElements` limits.

This placeholder keeps the folder under version control until phase 2 lands.
