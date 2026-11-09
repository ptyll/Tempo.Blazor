# Headless převodník BPMN 2.0 XML — návrh API, mapování a korpus

> Stav: schváleno — MODELING-BPMN-XML-API (phase 1 plánu `6fca9139-c871-41c4-c06e-08df38c30cbd`).
> Konzument: PromptHelper UC-PRC-017 (import/export BPMN 2.0 XML), nepřímo UC-PRC-018 a UC-PRC-026.
> Rozhodnutí DEC-PRC-001 body 10 a 12 (PH app `3c052519-a811-4c7e-b2de-d47088082b86`).

## 1. Cíle a ne-cíle

Převodník **BPMN 2.0 XML ⇄ `ModelingModelDto`** je **headless** (žádná závislost na
Blazor / Razor / JS interop), aby ho PromptHelper API mohlo použít na serveru.
Deterministický: stejný vstup ⇒ stejný výstup (stabilní ID, pořadí, serializace).
Nic se nezahazuje tiše: každá nepodporovaná nebo aproximovaná konstrukce končí v
reportu ztrát s umístěním v souboru.

Ne-cíle: rendering, UI, BPMN execution/simulace, editace diagramu.

## 2. Re-verifikovaný stav repozitáře (2026-11)

- **Cílový model**: `src/Tempo.Blazor.Abstractions/Modeling/ModelingDtos.cs` —
  `ModelingModelDto` (`Elements`, `Relationships`, `Views`, `Issues`, `Metadata`),
  `ModelingElementDto` (`SemanticType`, `ParentId` hierarchie, `Properties` jako
  `Dictionary<string, JsonElement>`, `SourceId`/`SourceType`/`SourcePath`),
  `ModelingRelationshipDto`, `ModelingViewDto` (`Nodes` s X/Y/Width/Height/
  `ParentNodeId`, `Connections` s `Waypoints`), `ModelingIssueDto`.
  `Tempo.Blazor.Abstractions` má nulové UI závislosti — vhodný jediný závislost převodníku.
- **Sémantický katalog**: `BpmnModelingCatalog` v `src/Tempo.Blazor.Modeling/Components/
  Modeling/BuiltInModelingProfiles.cs:1749` (internal) — elementy: `Task`, `UserTask`,
  `ServiceTask`, `SendTask`, `ReceiveTask`, `ScriptTask`, `BusinessRuleTask`, `ManualTask`,
  `SubProcess`, `EventSubProcess`, `CallActivity`, `StartEvent`, `EndEvent`,
  `IntermediateCatchEvent`, `IntermediateThrowEvent`, `BoundaryEvent`, `ExclusiveGateway`,
  `ParallelGateway`, `InclusiveGateway`, `ComplexGateway`, `EventBasedGateway`, `Pool`,
  `Lane`, `DataObject`, `DataStore`, `Group`, `TextAnnotation`. Vazby: `SequenceFlow`,
  `ConditionalFlow`, `DefaultFlow`, `MessageFlow`, `Association`, `DataInputAssociation`,
  `DataOutputAssociation`. Notace klíč `bpmn2` (`BuiltInModelingProfiles`).
  Převodník duplikuje tyto názvy jako vlastní konstanty (nechce závislost na internal typu);
  test ověří, že se katalog nerozjel (parity test).
- **Vzory**: `RdlReportImporter` (XDocument + namespace-agnostic local-name match,
  výsledek s diagnostikami, malformed XML ⇒ error výsledek, ne throw) a
  `Tempo.Blazor.DocumentFormats` (bezpečné `XmlReaderSettings`, importer/exporter
  dvojice). Převodník kopíruje jejich styl: `BpmnImportResult`/`BpmnExportResult`
  s issues, žádné výjimky za běžných chyb stavu vstupu.
- **Auto-layout**: `ModelingLayoutArranger` (Tempo.Blazor.Modeling) je deterministický
  čistě C# band-arrangement pro pool/lane + rankování flow uzlů, ale je `internal` a vázaný
  na `Tempo.Blazor.Components.Diagram.Models`. Dagre layout běží v prohlížeči (JS) —
  headless nedostupný. **Rozhodnutí**: převodník implementuje vlastní headless
  `BpmnAutoLayout` (fáze 3) podle stejné konvence (flush lane pásy, rank = nejdelší cesta
  od startu, declaration-order tie-break, `ceil(sqrt(n))` grid pro nezávislé uzly).

## 3. Umístění a balíčky

Nový balíček **`Tempo.Blazor.Modeling.BpmnXml`** (multi-target net8.0/net9.0/net10.0,
jako ostatní), jediná závislost `Tempo.Blazor.Abstractions`. Namespace
`Tempo.Blazor.Modeling.BpmnXml`. Žádný ASP.NET, Blazor, JS interop.
DI: `services.AddTempoBpmnXml()` registruje `IBpmnXmlImporter`/`IBpmnXmlExporter`
+ tool pluginy (fáze 5 rozšíří o MCP nástroje).

## 4. Mapovací tabulka BPMN 2.0 XML → sémantické typy

Element/attribut XML | SemanticType / chování
---|---
`process` (bez participant) | kořen modelu, Notation=`bpmn2`
`collaboration` | kořen; `participant` → element **Pool** (`ParentId` modelu), `processRef` ⇒ Pool odkazuje proces
`participant` (bez processRef / opaque) | Pool s loss záznamem "black-box pool"
`laneSet`/`lane`/`childLaneSet` | **Lane**, `ParentId` = nadřazená lane/pool; `flowNodeRef` ⇒ umístění uzlu
`task` | Task; `scriptTask` ScriptTask; `userTask` UserTask; `serviceTask` ServiceTask; `sendTask` SendTask; `receiveTask` ReceiveTask; `businessRuleTask` BusinessRuleTask; `manualTask` ManualTask
`subProcess` (`triggeredByEvent=false`) | SubProcess, děti s `ParentId`
`subProcess` (`triggeredByEvent=true`) | EventSubProcess (dtto hierarchie)
`subProcess` (`adHoc=true`) | SubProcess + loss "ad-hoc ordering not preserved" (dtto jako `adHocSubProcess`)
`adHocSubProcess` | SubProcess + loss "ad-hoc ordering not preserved" (`ordering`/`completionCondition` jdou do Properties, export je neobnoví — loss)
`transaction` | SubProcess + Properties `{"transaction":true}` + loss k compensaci (pokud má compensate věci)
`callActivity` | CallActivity; `calledElement(+Binding/Version)` → Properties
`startEvent`/`endEvent`/`intermediateCatchEvent`/`intermediateThrowEvent` | StartEvent/EndEvent/IntermediateCatchEvent/IntermediateThrowEvent; `eventDefinition*` → Properties (`eventDefinitions: [{type, ref, ...}]`), marker dle typu; `multiple`/`parallelMultiple` → Properties
`boundaryEvent` | BoundaryEvent; `attachedToRef` + `cancelActivity` → **Properties only** (žádná syntetizovaná vazba k hostiteli — attachment je vizuální/layout informace z DI)
`task` s `isForCompensation="true"` | Task; `isForCompensation` → Properties; kompenzační vazba = obyčejná `association` (importuje se jako Association)
`exclusiveGateway`/`parallelGateway`/`inclusiveGateway`/`complexGateway`/`eventBasedGateway` | ExclusiveGateway/ParallelGateway/InclusiveGateway/ComplexGateway/EventBasedGateway
`sequenceFlow` | SequenceFlow; `conditionExpression` ⇒ **ConditionalFlow**; target `default` brány ⇒ **DefaultFlow** (jinak SequenceFlow); obsah výrazu → Properties `condition`
`messageFlow` | MessageFlow (zdroj/cíl může být Pool, uzel nebo participantRef)
`association` | Association; `direction` → Properties
`dataInputAssociation`/`dataOutputAssociation` | DataInputAssociation/DataOutputAssociation; **implicitní endpoint = ohraničující aktivita** — chybí-li `sourceRef` (input) resp. `targetRef` (output), nahradí se ohraničující aktivitou; `sourceRef`/`targetRef` mířící na `property`/`ioSpecification dataInput`/`dataOutput` id se **resolvují na ohraničující aktivitu** (fixture `io-properties.bpmn`)
`dataObjectReference` | DataObject (`dataObjectRef` ⇒ dtto element); neodkazovaný `dataObject` se **importuje jako samostatný DataObject** + info-loss "unreferenced dataObject"; `isCollection` → Properties
`dataStoreReference` | DataStore (`dataStoreRef` ⇒ global dataStore do Properties)
`textAnnotation` | TextAnnotation, text v `Name`
`group` | Group; `categoryValueRef`/label → Properties
`documentation` | `Description` prvku (první dokument)
`extensionElements` | viz §6
`name`, `id`, `isExecutable` | Name, Id (SourceId), Properties
**catch-all (jakýkoli nemapovaný atribut)** | neznámý BPMN atribut → `Properties["bpmn:{localName}"]`; flagy z BPMNDI (`isExpanded`, `isHorizontal`, …) → `Properties["bpmndi:{name}"]`; export je píše zpět (round-trip bez ztráty)

Nepodporované konstrukce (vždy loss záznam, žádný throw): `choreographyTask`,
`subChoreography`, `callChoreography`, `conversation`, `subConversation`,
`callConversation`, `conversationLink`, `transaction` compensation detaily
(samotné `compensateEventDefinition` na boundary/end události importujeme: marker +
Properties + Association na `isForCompensation` úkol — viz řádky výše),
`multiInstanceLoopCharacteristics`,
`standardLoopCharacteristics`,
globální definice `message`/`signal`/`error`/`escalation` **se zachovávají** (id + name +
případný kód) v Metadata/Properties modelu, aby export mohl obnovit `<definitions>`
kořen i po čistém import→export cyklu; definice deklarované ale nikde nepoužité ⇒ info-loss
"definition declared but unused", `correlationSubscription`,
`globalConversation`, `partnerEntity/Role`, `monitoring`, `auditing`, IO specifikace
(`ioSpecification dataInput`/`dataOutput` jako endpointy asociací resolvujeme na
ohraničující aktivitu; samotná deklarace bez vazby ⇒ loss), `resourceRole`, `category`/`categoryValue`
samotné (skupiny importujeme, kategorie do Properties), `rendering`, DI `style`/`labelStyle`.
Saxová pravidla: chybějící povinný atribut (např. `sourceRef`) ⇒ loss + uzel/vazba
i tak importován, pokud je srozumitelný.

Každý loss záznam: `{ elementId?, xmlPath, construct, reason, severity }` (severity
Loss/Approximated/Info — mapuje se na `ModelingIssueDto` kategorií `bpmn-import`).

## 5. Veřejné API (schváleno — MODELING-BPMN-XML-API)

```csharp
namespace Tempo.Blazor.Modeling.BpmnXml;

public interface IBpmnXmlImporter
{
    BpmnImportResult Import(Stream xml, BpmnImportOptions? options = null, CancellationToken ct = default);
    BpmnImportResult Import(string xml, BpmnImportOptions? options = null);
}

public interface IBpmnXmlExporter
{
    BpmnExportResult Export(ModelingModelDto model, BpmnExportOptions? options = null, CancellationToken ct = default);
}

public sealed class BpmnImportOptions
{
    public BpmnModelSplit ModelSplit { get; set; } = BpmnModelSplit.PerProcess; // PerProcess | SingleWithPools
    public bool ImportBpmndi { get; set; } = true;        // false = pozice ignorovány (auto-layout fáze 3)
    public long MaxInputBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxDepth { get; set; } = 256;
    public int MaxElements { get; set; } = 500_000;
    public IList<IBpmnToolExtension> ToolExtensions { get; } = []; // Camunda/Signavio/Bizagi pluginy
}

public enum BpmnModelSplit { PerProcess, SingleWithPools }

public sealed class BpmnImportResult
{
    public IReadOnlyList<ModelingModelDto> Models { get; }
    public IReadOnlyList<BpmnImportLoss> Losses { get; }
    public IReadOnlyList<BpmnImportWarning> Warnings { get; }
    public bool HasErrors { get; }   // např. ne-well-formed XML, chybí process/collaboration
    public string? ErrorMessage { get; }
}

public sealed class BpmnExportOptions
{
    public BpmnExportProfile Profile { get; set; } = BpmnExportProfile.Standard; // Standard | WithHostExtensions
    public string HostExtensionNamespace { get; set; } = "https://tempo.blazor/schemas/bpmn-extensions";
    public string HostExtensionPrefix { get; set; } = "tempo";
    public bool EmitBpmndi { get; set; } = true;
    public string? TargetNamespace { get; set; }
}

public enum BpmnExportProfile { Standard, WithHostExtensions }

public sealed class BpmnExportResult
{
    public string? Xml { get; }
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyList<BpmnExportLoss> Losses { get; } // prvky modelu, které nemají BPMN obdobu
}
```

Rozhodnutí v návrhu:

1. **ModelSplit** — `PerProcess` (výchozí): každý `process` participantu ⇒ jeden
   `ModelingModelDto`, Pool je elementem modelu, messageFlowy vazbami mezi modely
   (výsledek `Models[i].Relationships` obsahují vazby, jejichž druhá strana patří jinému
   modelu, s `Properties["externalModelRef"]`). `SingleWithPools`: jeden model, pooly
   jako kořenové elementy.
2. **Bezpečnost** — `XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver = null }`,
   limity z options; porušení ⇒ `HasErrors` (povinné testy XXE + billion laughs ve fázi 2).
3. **Determinism** — ID zůstávají BPMN id; pro vygenerovaná ID deterministický sklon
   (`{type}_{stableHash}` z cesty); pořadí elementů = pořadí v XML; export řadí
   definice dle prvního výskytu. Golden testy round-trip ve fázích 2–4.
4. **Views** — import BPMNDI ⇒ jeden View na každý `BPMNPlane` (více diagramů =
   více Views, `Notation="bpmn2"`); Nodes nesou X/Y/W/H, connections Waypoints.
   `ParentNodeId` pro uzly vnořené do shape kontejneru (pool/lane/subprocess dle DI isExpanded).
5. **Stream API** — pro ASP.NET Core volající: `Import(Stream)` je sync wrapper, který
   sám čte s bounded buffer; HTTP caller může (nemusí) bufferovat body
   (`await Request.Body.CopyToAsync` s capem `MaxInputBytes`). Limit `MaxInputBytes` je
   vynucen bounded čtením, **ne** přes `Stream.Length` (stream nemusí být seekable).

## 6. Extenze (host + nástroje)

- **Host namespace**: při `Profile=WithHostExtensions` se `Properties` prvku/vazby
  serializují do
  `<extensionElements><tempo:properties><tempo:property name="key" value="JSON"/></tempo:properties></extensionElements>`.
  JSON je deterministický (`JsonSerializerOptions { WriteIndented=false }`, klíče
  seřazené — canonicalizace přes `JsonNode` sort). Při importu se neznámé
  extensionElements v host namespace vrací zpět do `Properties` (round-trip).
  Namespace `https://tempo.blazor/schemas/bpmn-extensions` **musí sedět** se schématem
  vlastností z navazujícího plánu „Plné BPMN authoring" (sdílený change set).
- **Tool pluginy**: `IBpmnToolExtension { string NamespaceUri; void OnImportElement(...); void OnExportElement(...); }`.
  Registrované built-in: **Camunda** (`http://camunda.org/schema/1.0/bpmn` —
  `assignee`/`candidateGroups`/`candidateUsers`/`formKey`/`dueDate`/`versionTag`/
  `delegateExpression` ⇒ Properties klíče `camunda:*`; `camunda:documentation` ⇒
  Description; ostatní `extensionElements` ⇒ Properties `camundaExtensions` jako XML string),
  **Signavio** (`http://www.signavio.com` — `signavioMetaData/@metaKey/@metaValue` ⇒
  Properties `signavio:{key}`; diagram meta-data ⇒ Metadata/Properties modelu),
  **Bizagi** (`http://www.bizagi.com/schemas/2010/Bizagi` — `BizagiProperty/@name/@value`
  ⇒ Properties `bizagi:{name}`; atributy `Bizagi:property` ⇒ Properties `bizagi:property`).
  Neznámé namespace ⇒ loss záznam "unrecognized extension namespace", obsah zachován
  jako `Properties["foreignExtensions"][ns]` (XML string) — nic se nezahazuje.

## 7. Testovací korpus

`tests/Tempo.Blazor.Tests/Modeling/BpmnCorpus/` — 20 hand-authored fixtur (MIT, viz
README složky) imitujících dialektů reálných nástrojů: jednoduchý proces, collaboration
se 3 pooly a messageFlow, vnořené lanes, všechny brány, všechny události s eventDefinitions,
boundary events (incl. transaction+cancel), event subprocess, callActivity, datové objekty,
model bez BPMNDI, Camunda/Signavio/Bizagi export, textAnnotation/association/group,
soubor záměrně nepodporovaných konstrukcí (incl. top-level `adHocSubProcess` a kompenzace),
prefixed-qnames dialekt (`bpmn2:` + `tns:` QName refy), subprocess vnořený 3 úrovně,
collapsed subprocess s vlastním druhým BPMNDiagram/BPMNPlane, black-box participant
bez `processRef`, `multiple`/`parallelMultiple` události se dvěma eventDefinitiony
a bpmn.io-style `ioSpecification`/`<property>`. Podadresář `Hostile/` je vyhrazen pro
fázi 2 (XXE, billion laughs, oversized streamy) a záměrně leží mimo `ExpectedCorpusFiles`.
Guard testy: `BpmnCorpusTests` (well-formed, bezpečný parse, unikátní NCName id,
dangling reference guard vč. element-text referencí s odstraněným QName prefixem,
BPMNDI presence, parity konstant proti `BpmnModelingCatalog` přes InternalsVisibleTo).

## 8. Fázování (připomínka)

- Fáze 2: bezpečný parser + import sémantiky + loss report (TDD golden testy na korpusu;
  Hostile/ fixturny pro XXE/billion-laughs/limity; parity konstant je už teď hlídána
  `BpmnCorpusTests.Converter_semantic_type_constants_match_BpmnModelingCatalog`).
- Fáze 3: BPMNDI import do Views + headless `BpmnAutoLayout` pro modely bez DI.
- Fáze 4: export + round-trip (import(export(x)) ≅ x pro korpus bez lossů).
- Fáze 5: tool pluginy, MCP nástroje (`Tempo.Blazor.Mcp/Modeling`), demo.
- Fáze 6: docs, CHANGELOG, JSON doc overlay pokud přibyde komponenta (zatím ne).
