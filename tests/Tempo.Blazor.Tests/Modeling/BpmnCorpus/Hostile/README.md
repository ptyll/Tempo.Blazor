# Hostile BPMN fixtures (phase 2)

Adversarial security fixtures for the headless BPMN 2.0 XML importer
(plan `6fca9139-c871-41c4-c06e-08df38c30cbd`, phase 2). They live OUTSIDE
`ExpectedCorpusFiles` in `BpmnCorpusTests` (they must NOT import cleanly) and drive
the mandatory `HasErrors` tests for the safe-loading rules.

| Fixture | Rule it probes |
| --- | --- |
| `xxe.bpmn` | External-entity XXE payload — DTD is prohibited, so the import must fail without resolving `file:///etc/passwd`. |
| `billion-laughs.bpmn` | Classic nested-entity exponential expansion (billion laughs) — must fail at the DTD, never expanding. |
| `malformed.bpmn` | Not well-formed XML — the error message must carry the line number. |
| `wrong-root.bpmn` | Document whose root is not `definitions` — rejected as not-a-BPMN-document. |
| `oversized.bpmn` | ~3.7 KB document; with a lowered `MaxInputBytes` the bounded stream must abort. |
| `too-deep.bpmn` | 40 nested subprocesses; with a lowered `MaxDepth` the import must abort. |
| `too-many-elements.bpmn` | 40+ tasks; with a lowered `MaxElements` the import must abort. |
| `missing-assignment-endpoints.bpmn` | Well-formed document whose `assignment` has neither `from` nor `to` — the semantic walk must not throw; the import succeeds with empty assignment endpoints. |
| `collaboration-only-message-flows.bpmn` | Collaboration with no process/participants but a `messageFlow` — the importer synthesizes a collaboration model instead of crashing on an empty model list. |

All fixtures are hand-authored for this repository (MIT, same as the corpus).
