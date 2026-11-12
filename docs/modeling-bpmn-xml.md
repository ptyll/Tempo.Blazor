# BPMN 2.0 XML konvertor — uživatelská příručka

> Balíček: `Tempo.Blazor.Modeling.BpmnXml` (headless, multi-target net8.0/net9.0/net10.0).
> Rozhodnutí: MODELING-BPMN-XML-API (plán `6fca9139-c871-41c4-c06e-08df38c30cbd`).
> Konzument: PromptHelper UC-PRC-017 (import/export BPMN 2.0 XML), nepřímo UC-PRC-018 a UC-PRC-026;
> rozhodnutí DEC-PRC-001 body 10 a 12 (PH app `3c052519-a811-4c7e-b2de-d47088082b86`).
> Verze: 2.11.0 (vydává se společně s plánem „Plné BPMN authoring").

Převodník **BPMN 2.0 XML ⇄ `ModelingModelDto`** je **headless** (žádná závislost na
Blazor / Razor / ASP.NET / JS interop) — jediná závislost je `Tempo.Blazor.Abstractions`,
takže ho lze použít v konzolových aplikacích, worker službách, ASP.NET Core API i MCP
serverech. Je **deterministický** (stejný vstup ⇒ stejný výstup: stabilní ID z BPMN id,
invariant-culture čísla, LF konce řádků, žádný BOM) a **nikdy tiše nezahazuje obsah**:
každá nepodporovaná nebo aproximovaná konstrukce končí v reportu ztrát s umístěním
v souboru.

Ne-cíle: rendering, UI, BPMN execution/simulace, editace diagramu.

## 1. Instalace a registrace

```bash
dotnet add package Tempo.Blazor.Modeling.BpmnXml
```

**Dependency injection** (registruje importer, exporter i built-in tool pluginy):

```csharp
builder.Services.AddTempoBpmnXml();

public class MyService(IBpmnXmlImporter importer, IBpmnXmlExporter exporter) { … }
```

**Bez DI** — parameterless konstruktory zůstávají bez pluginů; pluginy lze předat explicitně:

```csharp
var importer = new BpmnXmlImporter(BpmnBuiltInToolExtensions.All);
var exporter = new BpmnXmlExporter();
```

DI-resolvené importer/exporter používají registrované pluginy automaticky (explicitně
naplněný `ToolExtensions` v options vždy vyhraje; sloučený seznam se de-duplikuje
podle `NamespaceUri`, first-wins).

## 2. Import

```csharp
var importer = new BpmnXmlImporter();

// ze streamu (nemusí být seekable — čte se s bounded bufferem):
BpmnImportResult result = importer.Import(stream, new BpmnImportOptions
{
    ModelSplit = BpmnModelSplit.PerProcess,   // výchozí; SingleWithPools = jeden model
    ImportBpmndi = true,                       // výchozí; false = pozice ignorovány, auto-layout
    MaxInputBytes = 10 * 1024 * 1024,          // výchozí
    MaxDepth = 256,                            // výchozí
    MaxElements = 500_000,                     // výchozí
});

// nebo z řetězce:
BpmnImportResult result = importer.Import(xml);

if (result.HasErrors)
{
    // result.ErrorMessage — importer NIKDY nehází výjimku pro špatný vstup
    return;
}

IReadOnlyList<ModelingModelDto> models = result.Models;
```

- `PerProcess` (výchozí): každý `process` participantu ⇒ jeden `ModelingModelDto`;
  cross-model `messageFlow` jsou vazbami s `Properties["externalModelRef"]`.
- `SingleWithPools`: jeden model, pooly jako kořenové elementy.
- Výsledek nese `Losses` (report ztrát, viz §5), `Warnings` a modely s `Issues`
  kategorie `bpmn-import` (strukturální validace: dangling flow endpointy, zlomené
  boundary attachmenty, start/end-event flow pravidla, duplicitní id).

## 3. Export

```csharp
var exporter = new BpmnXmlExporter();

// jeden model, nebo více modelů ⇒ JEDEN <definitions> s <collaboration>
// (pooly ⇒ participanti, cross-model messageFlowy ⇒ <messageFlow>):
BpmnExportResult exported = exporter.Export(models, new BpmnExportOptions
{
    Profile = BpmnExportProfile.Standard,          // výchozí; viz níže
    HostExtensionNamespace = "https://tempo.blazor/schemas/bpmn-extensions",
    HostExtensionPrefix = "tempo",
    EmitBpmndi = true,                              // výchozí
    TargetNamespace = null,                         // výchozí; vlastní targetNamespace
    // ToolExtensions — export hooky pluginů (DI fallback jako u importeru)
    // ExtensionNamespaces — prefix → URI mapa pro cizí atributové property
});

if (exported.Success)
{
    string xml = exported.Xml!;        // deterministic: LF, no BOM, invariant numbers
}
// exported.Losses — prvky modelu bez BPMN obdoby (viz §5)
```

**Profily:**

- `Standard` (výchozí): píše jen nativní BPMN; vše, co BPMN nemá (host property mimo
  catch-all, `Tags`/`Governance`, …) se objeví v `Losses`.
- `WithHostExtensions`: každou ne-native property serializuje do
  `<extensionElements><tempo:properties><tempo:property name="key" value="JSON"/>…`
  pod host namespace (deterministický JSON: neindentovaný, seřazené klíče). Import
  extensionElements v host namespace vrací zpět do `Properties` — **round-trip bez ztráty**.
  Namespace `https://tempo.blazor/schemas/bpmn-extensions` je sdílený se schématem
  vlastností plánu „Plné BPMN authoring" — neměnit.

## 4. Options a limity

Import (`BpmnImportOptions`) | Výchozí | Význam
---|---|---
`ModelSplit` | `PerProcess` | Rozdělení collaboration na modely
`ImportBpmndi` | `true` | Import BPMNDI do Views; `false` = auto-layout
`MaxInputBytes` | 10 MiB | Bound čtení vstupu (stream nemusí být seekable)
`MaxDepth` | 256 | Maximální hloubka zanoření
`MaxElements` | 500 000 | Maximální počet elementů
`ToolExtensions` | `[]` | Tool pluginy (DI fallback first-wins de-dup)

Export (`BpmnExportOptions`) | Výchozí | Význam
---|---|---
`Profile` | `Standard` | `Standard`/`WithHostExtensions`
`HostExtensionNamespace`/`HostExtensionPrefix` | `https://tempo.blazor/schemas/bpmn-extensions` / `tempo` | Host extension namespace
`EmitBpmndi` | `true` | Zápis BPMNDI (jeden plane na distinct view id)
`TargetNamespace` | `null` | Vlastní `targetNamespace` kořene
`ToolExtensions` | `[]` | Export hooky pluginů
`ExtensionNamespaces` | Camunda/Signavio/Bizagi pre-seed | Prefix → URI pro cizí atributové property; neznámé prefixy ⇒ deterministicky `urn:tempo-blazor:bpmn:{prefix}` s Info loss

Výkon: import je O(n) (200 000 flat tasků do sekund pod výchozími limity); export
5000 tasků + 5000 flowů < 2 s; auto-layout O(V+E) iterativní (100k chain < 5 s).

## 5. Report ztrát

Povinné pravidlo: **nic se nezahazuje tiše.**

- Import: `BpmnImportResult.Losses` — `BpmnImportLoss { elementId?, xmlPath, construct,
  reason, severity }`, severity `Loss`/`Approximated`/`Info`; mirror jako `bpmn-import`
  issues na vlastnícím modelu.
- Export: `BpmnExportResult.Losses` — `BpmnExportLoss { elementId?, construct, reason,
  severity }`.
- Neznámé extension namespace: obsah zachován verbatim pod
  `Properties["foreignExtensions"][ns]` (XML string) + loss záznam.

Typické import lossy: choreografie/conversation konstrukce, `multiInstance`/`standard`
loop characteristics, ad-hoc ordering, transaction compensation detaily, nepoužité
globální definice, `BPMNLabelStyle` fontové definice, `di:extension` payloady.
Typické export lossy (`Standard` profil): host property, `Tags`/`Governance`,
nevalidní NCName property klíče, eventDefinition bez typu.

## 6. Extenze

### 6.1 Host extension namespace

Profil `WithHostExtensions` píše/čte properties pod host namespace (viz §3). Import
vrací neznámé extensionElements v host namespace zpět do `Properties` (round-trip).

### 6.2 Tool pluginy (`IBpmnToolExtension`)

```csharp
public interface IBpmnToolExtension
{
    string NamespaceUri { get; }                    // namespace, který plugin vlastní
    bool OwnsAttributeExport => false;              // true = plugin serializuje sám (payload shape)
    void OnImportElement(BpmnToolImportContext context);   // extensionElements → Properties
    void OnExportElement(BpmnToolExportContext context);   // Properties → extensionElements
}
```

Built-in pluginy (`BpmnBuiltInToolExtensions.All`, registruje `AddTempoBpmnXml()`):

- **Camunda** (`http://camunda.org/schema/1.0/bpmn`): `assignee`/`candidateGroups`/
  `candidateUsers`/`formKey`/`dueDate`/`versionTag`/`delegateExpression` ⇒ `camunda:*`
  property klíče; `camunda:documentation` ⇒ Description; ostatní payloady (listenery,
  konektory, input/output) verbatim pod `camundaExtensions` — loss-free re-export.
- **Signavio** (`http://www.signavio.com`): `signavioMetaData` ⇒ `signavio:*` klíče;
  diagram meta-data ⇒ `signavio:*` property modelu. `OwnsAttributeExport = true`.
- **Bizagi** (`http://www.bizagi.com/schemas/2010/Bizagi`): `BizagiProperty` name/value
  ⇒ `bizagi:*` klíče. `OwnsAttributeExport = true`.

Neznámé namespace ⇒ loss „unrecognized extension namespace" + verbatim obsah pod
`foreignExtensions` — nic se nezahazuje.

## 7. MCP nástroje

Balíček `Tempo.Blazor.Mcp` (nástroje `modeling_import_bpmn` / `modeling_export_bpmn`)
používá tento převodník; popisný katalog včetně shape odpovědí je
[docs/modeling-mcp-tools.md](docs/modeling-mcp-tools.md) §`modeling_import_bpmn` /
§`modeling_export_bpmn`:

- `modeling_import_bpmn` — import BPMN XML (max 2M znaků) ⇒ modely + plný loss report;
  volitelný `save=true` persistuje každý model přes document provider (nezávisle,
  s přesným partial progress `saved`/`failedAt`/`failed`); malformed XML ⇒
  `validation_failed`.
- `modeling_export_bpmn` — export uloženého modelu (`modelId`; volitelně `profile`, `emitBpmndi`, `targetNamespace`) ⇒ XML + export loss report;
  profily `Standard`/`WithHostExtensions`. Write klasifikace: `modeling_import_bpmn`
  s `save=true` je write nástroj.

## 8. Bezpečnost

- **Bezpečné XML je výchozí**: `XmlReaderSettings { DtdProcessing = Prohibit,
  XmlResolver = null }` — DTD/entity se nezpracovávají vůbec (XXE a billion laughs
  testy jsou povinné: `BpmnXmlImporterSecurityTests`, adversarial fixture složka
  `tests/Tempo.Blazor.Tests/Modeling/BpmnCorpus/Hostile/`).
- **Limity** (`MaxInputBytes`/`MaxDepth`/`MaxElements`) se vynucují bounded čtením —
  stream nemusí být seekable; bounded reader nikdy nedisposeuje cizí stream a ctí
  cancellation.
- **Nikdy nehází výjimky** za běžných chyb vstupu: malformed XML ⇒ `HasErrors` +
  `ErrorMessage` (s číslem řádky). Neočekávané selhání load/build ⇒ generický
  `HasErrors` výsledek.
- HTTP caller může (nemusí) bufferovat body s vlastním capem před `Import(Stream)`.
- Balíček nemá žádný IO, síť ani proces — čistá transformace dat v paměti.

## 9. Interoperabilita a XSD (opt-in)

- **`npm run test:bpmn-interop`** — povinná kontrola interoperability: `bpmn-moddle`
  fromXML nad všemi export goldeny (`tests/Tempo.Blazor.Tests/Modeling/BpmnCorpus/Expected/*.export.bpmn`),
  zero warnings, root `bpmn:Definitions`. Ověřuje, že export čtou i jiné nástroje
  (bpmn.io/Camunda Modeler stack).
- **XSD validace (opt-in)** — test se spustí jen když proměnná `TM_BPMN_XSD_DIR`
  ukazuje na BPMN 2.0.2 XSD soubory (licence XSD pro bundling není ověřená, proto
  nejsou v repu); bez proměnné se test přeskočí.
- Ruční vizuální kontrola: otevřít např. `simple-process.export.bpmn` a
  `collaboration-3-pools.export.bpmn` z `Expected/` v bpmn.io nebo Camunda Modeler.

## 10. Mapovací tabulka BPMN 2.0 XML → sémantické typy

Cílový model: `src/Tempo.Blazor.Abstractions/Modeling/ModelingDtos.cs` —
`ModelingModelDto` (`Elements`, `Relationships`, `Views`, `Issues`, `Metadata`),
`ModelingElementDto` (`SemanticType`, `ParentId` hierarchie, `Properties` jako
`Dictionary<string, JsonElement>`, `SourceId`/`SourceType`/`SourcePath`),
`ModelingRelationshipDto`, `ModelingViewDto` (`Nodes` s X/Y/Width/Height/
`ParentNodeId`, `Connections` s `Waypoints`), `ModelingIssueDto`. Notace klíč `bpmn2`.

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
Properties + Association na `isForCompensation` úkol), `multiInstanceLoopCharacteristics`,
`standardLoopCharacteristics`, globální definice `message`/`signal`/`error`/`escalation`
**se zachovávají** (id + name + případný kód) v Metadata/Properties modelu, aby export
mohl obnovit `<definitions>` kořen i po čistém import→export cyklu; definice deklarované
ale nikde nepoužité ⇒ info-loss "definition declared but unused", `correlationSubscription`,
`globalConversation`, `partnerEntity/Role`, `monitoring`, `auditing`, IO specifikace
(samotná deklarace bez vazby ⇒ loss), `resourceRole`, `category`/`categoryValue`
samotné, `rendering`, DI `style`; `BPMNLabelStyle` fontové definice a `di:extension`
payloady (reference `labelStyle` se jako `bpmndi:labelStyle` property zachovává).
Saxová pravidla: chybějící povinný atribut (např. `sourceRef`) ⇒ loss + uzel/vazba
i tak importován, pokud je srozumitelný.

Každý loss záznam: `{ elementId?, xmlPath, construct, reason, severity }` (severity
Loss/Approximated/Info — mapuje se na `ModelingIssueDto` kategorií `bpmn-import`).

## 11. BPMNDI, auto-layout a export detailů

- **Waypointy view connections = plná absolutní DI polyline včetně koncových bodů**;
  import ji ukládá i na vazbu do `Properties["bpmndi:waypoints"]` +
  `Properties["bpmndi:planeId"]`, aby ji export napsal zpět beze ztráty.
- **Connection do view jen když jsou oba konce uzly tohoto view** — cross-model
  `messageFlow` nese geometrii na vazbě; generátor nikdy nehlásí missing-end issue
  pro importované view.
- **Auto-layout (`BpmnAutoLayout.Layout`) nechává waypointy prázdné** — generátor hranu
  sám trasuje. Částečný DI fill syntetizuje alespoň koncové body (`Route` přes DI pozice).
- **Částečný DI fill elementů:** každý chybějící element doplní jen view, které drží jeho
  **nejbližšího sémantického předka** (fallback `Views[0]`), s offsetem pod DI bounds —
  žádný překryv a žádný duplicitní `ElementId` napříč view. Model bez plane dostane
  úplný auto-layout view s varováním.
- **DI flagy a labely:** atributy BPMNShape/BPMNEdge ⇒ `bpmndi:*` property;
  `BPMNLabel` bounds ⇒ `bpmndi:labelBounds`; `labelStyle` reference ⇒ `bpmndi:labelStyle`.
  `BPMNLabelStyle` definice a `di:extension` payloady ⇒ loss report.
- **Auto-layout konvence:** O(V+E) (iterativní Kahnův topologický sort, žádná rekurze —
  50k reverz deklarovaný chain neoverflowuje); rank = nejdelší cesta od startu přes
  non-back hrany; vrstvy centrovány na nejvyšší vrstvu (chain = jedna horizontální linka);
  **boundary event sedí ve sloupci hostitelské aktivity na samostatném řádku** (weight-0
  hrana do její vrstvy); pooly = horizontální pásy s flush lane pásy uvnitř.
- **Multi-model export** píše JEDEN dokument: každý model ⇒ `<process>`, pooly ⇒
  participanti, cross-model messageFlowy ⇒ `<messageFlow>` v `<collaboration>`;
  prázdná `<collaboration/>` se neemituje nikdy. View geometry = jeden `BPMNPlane` na
  distinct view id; auto-layouted connections se syntetizují koncové body.
- **Schéma-validní výstup:** kořen deklaruje `xmlns:xsi`; výrazy nesou
  `xsi:type="tFormalExpression"`; `<process>` vždy nese `isExecutable`; `definitions`
  nese `exporter="Tempo.Blazor"` + `exporterVersion` (holá release verze — prerelease/
  build-metadata přípony, vč. CI stampu `-ci-<timestamp>` z `-p:Version`, se usekávají,
  aby export zůstal byte-for-byte reprodukovatelný); flow nodes nesou Camunda-style
  `<incoming>`/`<outgoing>`; per každý `dataObjectRef` se syntetizuje jeden
  `<dataObject id name isCollection>`; DI id `{id}_di` (kolize ⇒ `{id}_di_1`).
- **Robustnost exportu** (nikdy nehází pro špatný modelový obsah): nevalidní NCName
  property klíče ⇒ loss; invalid XML 1.0 znaky ⇒ strip + `Approximated` loss;
  eventDefinition bez/ s neznámým typem ⇒ loss. Duplicitní id elementů napříč modely
  ⇒ první výskyt (sdílená instance není duplicita); duplicitní id vazby ⇒ loss.
  Prefixy generované z URI jsou stabilní FNV-1a hashe; rezervované prefixy
  (`bpmndi`/`dc`/`di`/`xsi`/`xml`/host) se nikdy nezastíní — kolize ⇒ `{prefix}_1`
  s Info loss. `Tags`/`Governance` (element) a tagy vazeb: `WithHostExtensions` ⇒ host
  payload, `Standard` ⇒ loss. Root extensionElements v host namespace obnovují
  model-level properties.

## 12. Veřejné API (schváleno — MODELING-BPMN-XML-API)

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
    BpmnExportResult Export(IReadOnlyList<ModelingModelDto> models, BpmnExportOptions? options = null, CancellationToken ct = default);
}

public sealed class BpmnImportOptions
{
    public BpmnModelSplit ModelSplit { get; set; } = BpmnModelSplit.PerProcess; // PerProcess | SingleWithPools
    public bool ImportBpmndi { get; set; } = true;        // false = pozice ignorovány (auto-layout)
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
    public IList<IBpmnToolExtension> ToolExtensions { get; } = []; // export hooky pluginů
    // prefix → URI mapa pro cizí atributové property (camunda:assignee, …); neznámé prefixy se
    // deklarují deterministicky jako urn:tempo-blazor:bpmn:{prefix} s Info loss
    public IDictionary<string, string> ExtensionNamespaces { get; }
}

public enum BpmnExportProfile { Standard, WithHostExtensions }

public enum BpmnExportLossSeverity { Loss, Approximated, Info }

public sealed class BpmnExportLoss
{
    public string? ElementId { get; init; }   // id elementu/vazby, když konstrukt nese
    public required string Construct { get; init; }  // Property | Tags | Governance | Namespace | …
    public required string Reason { get; init; }
    public BpmnExportLossSeverity Severity { get; init; }
}

public sealed class BpmnExportResult
{
    public string? Xml { get; }
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyList<BpmnExportLoss> Losses { get; } // prvky modelu, které nemají BPMN obdobu
}
```

Rozhodnutí v návrhu:

1. **Determinismus** — ID zůstávají BPMN id; pro vygenerovaná ID deterministický sklon
   (`{type}_{stableHash}` z cesty); pořadí elementů = pořadí v XML; export řadí definice
   dle prvního výskytu. Golden testy round-trip (import→export→import ≅ model;
   export(import(export(x))) byte-identické).
2. **Stream API** — `Import(Stream)` je sync wrapper čtoucí s bounded buffer; limit
   `MaxInputBytes` je vynucen bounded čtením, **ne** přes `Stream.Length`.
3. **Views** — import BPMNDI ⇒ jeden View na každý `BPMNPlane` (více diagramů = více
   Views, `Notation="bpmn2"`); Nodes nesou X/Y/W/H, connections Waypoints, `ParentNodeId`
   pro uzly vnořené do shape kontejneru (pool/lane/subprocess dle DI isExpanded).

## 13. Testovací korpus a testování

`tests/Tempo.Blazor.Tests/Modeling/BpmnCorpus/` — 23 hand-authored fixtur (MIT, viz
README složky) imitujících dialektů reálných nástrojů (Camunda Modeler, Signavio,
Bizagi, bpmn.io): jednoduchý proces, collaboration se 3 pooly a messageFlow, vnořené
lanes, všechny brány, všechny události s eventDefinitions, boundary events (incl.
transaction+cancel), event subprocess, callActivity, datové objekty, model bez BPMNDI,
Camunda/Signavio/Bizagi export, textAnnotation/association/group, soubor záměrně
nepodporovaných konstrukcí, prefixed-qnames dialekt, subprocess vnořený 3 úrovně,
collapsed subprocess s vlastním druhým BPMNDiagram/BPMNPlane, black-box participant,
`multiple`/`parallelMultiple` události, bpmn.io-style `ioSpecification`/`<property>` a
`partial-bpmndi.bpmn` (DI pokrývá jen část uzlů/hran). Podadresář `Hostile/` — XXE,
billion laughs, malformed XML, wrong root, size/depth/element-count sondy.

Guard testy: `BpmnCorpusTests` (well-formed, bezpečný parse, unikátní NCName id,
dangling reference guard vč. element-text referencí, BPMNDI presence, parity konstant
proti `BpmnModelingCatalog`). Složka `Expected/` nese import goldeny (`*.import.json`)
i export goldeny (`*.export.bpmn`) — regenerace `TEMPO_REGENERATE_BPMN_IMPORT_GOLDEN=1` /
`TEMPO_REGENERATE_BPMN_EXPORT_GOLDEN=1`.

Relevantní sady: `BpmnXmlImporterSecurityTests`, `BpmnXmlImporterTests`,
`BpmnImportGoldenTests`, `BpmnViewImportTests`, `BpmnAutoLayoutTests`,
`BpmnViewRenderTests`, `BpmnXmlExporterTests`, `BpmnExportGoldenTests`,
`BpmnRoundTripTests`, `BpmnToolExtensionTests`, `BpmnPackageBoundaryTests`
(headless boundary: jen `Tempo.Blazor.Abstractions`, žádný UI/server stack v uzávěru),
`ModelingBpmnToolsTests` (MCP), E2E `ModelingBpmnXmlConverterE2ETests` /
`ModelingBpmnDiRenderingE2ETests` / `ModelingBpmnServerE2ETests`.
