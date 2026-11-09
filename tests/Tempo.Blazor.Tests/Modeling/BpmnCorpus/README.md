# BPMN 2.0 test corpus (Tempo.Blazor Modeling)

All `.bpmn` files in this folder are **hand-authored by the Tempo.Blazor contributors**
for the headless BPMN 2.0 converter test suite (plan
`6fca9139-c871-41c4-c06e-08df38c30cbd`). They are original works, not exports of
proprietary diagrams, and are licensed under the same license as the Tempo.Blazor
repository (MIT). No third-party content is embedded.

The files mimic the XML shapes produced by real tools (Camunda Modeler, Signavio,
Bizagi Modeler, bpmn.io) so importer loss-reporting and extension handling can be
tested against realistic dialects, without shipping any tool's copyrighted diagrams.

| File | Covers |
|------|--------|
| `simple-process.bpmn` | Single process, BPMNDI with labels |
| `collaboration-3-pools.bpmn` | Collaboration, 3 participants, messageFlow, lanes |
| `nested-lanes.bpmn` | laneSet with childLaneSet nesting |
| `all-gateways.bpmn` | exclusive/parallel/inclusive/complex/eventBased gateways |
| `all-events.bpmn` | All event kinds with inline eventDefinitions |
| `boundary-events.bpmn` | Interrupting + non-interrupting boundary events, transaction + cancel |
| `event-subprocess.bpmn` | subProcess triggeredByEvent |
| `call-activity.bpmn` | callActivity with calledElement attributes |
| `data-objects.bpmn` | dataObject/dataObjectReference/dataStoreReference + associations |
| `no-bpmndi.bpmn` | Semantic model without any BPMNDI section |
| `camunda-export.bpmn` | `camunda:` extensionElements + attributes (assignee, candidateGroups, formKey) |
| `signavio-export.bpmn` | Signavio namespace attributes + extensionElements |
| `bizagi-export.bpmn` | Bizagi namespace attributes + extensionElements |
| `annotations-group.bpmn` | textAnnotation, association, group, category |
| `unsupported-constructs.bpmn` | Choreography/conversation/adHoc/multiInstance, top-level adHocSubProcess, compensation (boundaryEvent + isForCompensation + association) |
| `prefixed-qnames.bpmn` | Prefixed dialect: `bpmn2:` elements, `tns:` QName refs (`messageRef="tns:…"`) |
| `nested-subprocess.bpmn` | SubProcess nested 3 levels deep (ParentId hierarchy from XML nesting) |
| `collapsed-subprocess.bpmn` | Collapsed subprocess (`isExpanded="false"`) with its own second BPMNDiagram/BPMNPlane |
| `black-box-pool.bpmn` | Black-box participant without `processRef`, messageFlow to the pool |
| `multiple-events.bpmn` | `multiple` / `parallelMultiple` catch events with two eventDefinitions each |
| `io-properties.bpmn` | bpmn.io-style `ioSpecification` (dataInput/dataOutput/inputSet/outputSet), `<property>` ids as association targets |

## Hostile/

The `Hostile/` subfolder is reserved for phase-2 security fixtures (XXE, billion
laughs, oversized streams). Those files deliberately FAIL the safe-parse guards, so
they are kept outside `ExpectedCorpusFiles` in `BpmnCorpusTests`. See `Hostile/README.md`.
