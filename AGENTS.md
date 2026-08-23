# Tempo.Blazor – AI Agent Guide

## Project Overview

**Tempo.Blazor** is a comprehensive Blazor component library with 126+ reusable Razor components designed for AI-assisted development. The library provides a complete UI toolkit for building modern Blazor applications with support for multiple render modes (WebAssembly, Server, InteractiveAuto), localization, theming (light/dark), FluentValidation integration, and a CSS design system based on custom properties.

### Key Features
- **125+ reusable Razor components** organized into 28 categories (inputs, data tables, pickers, layout, feedback, charts, dashboards, workflow designer, etc.)
- **Multi-target .NET support**: .NET 8.0, 9.0, and 10.0
- **Full localization support** via `ITmLocalizer` (English + Czech built-in, extensible)
- **CSS design system** with CSS custom properties (`--tm-*` tokens)
- **Dark mode support** via `ThemeService`
- **FluentValidation integration** (optional separate package)
- **Icon extensibility** via `IconRegistry` and `IIconProvider`
- **WCAG 2.1 AA accessibility compliance**

## Technology Stack

| Category | Technology | Version |
|----------|------------|---------|
| Framework | .NET | 8.0, 9.0, 10.0 |
| UI Framework | Blazor (WASM, Server, InteractiveAuto) | Latest |
| Language | C# | 12 (latest) |
| Styling | CSS Custom Properties (Design Tokens) | - |
| Validation | FluentValidation | 12.1.1 |
| Localization | Microsoft.Extensions.Localization | Matching .NET version |
| Unit Testing | xUnit + bUnit | xUnit 2.9.3, bUnit 1.38.5 |
| E2E Testing | Playwright + MSTest | Playwright 1.51.0 |
| Assertions | FluentAssertions | 8.4.0 |
| Mocking | NSubstitute | 5.3.0 |

## Project Structure

```
TempoBlazor.slnx
├── src/
│   ├── Tempo.Blazor.Abstractions/    # Interfaces and models (NuGet package)
│   ├── Tempo.Blazor/                 # Core component library (NuGet package)
│   ├── Tempo.Blazor.Signing/         # Signing workflows and PDF template designer
│   ├── Tempo.Blazor.FluentValidation/# Optional FluentValidation integration
│   ├── Tempo.Blazor.Demo/            # Blazor WASM demo application
│   ├── Tempo.Blazor.Demo.Shared/     # Shared DTOs between API and Demo
│   ├── Tempo.Blazor.Demo.SharedUI/   # Shared UI components for all demos
│   ├── Tempo.Blazor.Demo.Api/        # ASP.NET Core Minimal API for demo data
│   ├── Tempo.Blazor.Demo.Server/     # Blazor Server demo application
│   └── Tempo.Blazor.Demo.InteractiveAuto/ # InteractiveAuto render mode demo
├── tests/
│   ├── Tempo.Blazor.Tests/           # bUnit component tests
│   ├── Tempo.Blazor.E2E/             # Playwright end-to-end tests
│   ├── Tempo.Blazor.Demo.Api.Tests/  # API integration tests
│   └── Tempo.Blazor.FluentValidation.Tests/  # Validation tests
└── .github/workflows/                 # CI/CD pipelines
```

### Project Dependencies

```
Tempo.Blazor.Abstractions (zero UI dependencies)
    ↑
Tempo.Blazor ────────┐
    ↑                │
Tempo.Blazor.FluentValidation (optional)
    ↑                │
Tempo.Blazor.Demo ◄──┘
    ↑
Tempo.Blazor.Demo.Shared ← Tempo.Blazor.Demo.Api
```

## Build and Test Commands

### Prerequisites
- .NET SDK 8.0, 9.0, and 10.0 installed
- For E2E tests: Playwright browsers installed (`playwright install`)

### Build
```bash
# Build entire solution
dotnet build TempoBlazor.slnx

# Build specific project
dotnet build src/Tempo.Blazor/Tempo.Blazor.csproj

# Build in Release mode (creates NuGet packages)
dotnet build -c Release
```

### Test
```bash
# Run all tests
dotnet test

# Run with verbosity
dotnet test --verbosity normal

# Run specific test project
dotnet test tests/Tempo.Blazor.Tests/
dotnet test tests/Tempo.Blazor.E2E/

# E2E lanes (see docs/e2e-test-lanes.md)
scripts/run-e2e-smoke.ps1   # PR gate: TestCategory=Smoke, < 20 min
scripts/run-e2e-full.ps1    # nightly: entire suite (~1700 tests, hours)

# JS engine unit tests (explicit file enumeration, no globs)
npm run test:document-editor
npm run test:reporting-modules
```

### Package Creation
```bash
# Create NuGet packages
dotnet pack src/Tempo.Blazor.Abstractions/ -c Release -o ./packages
dotnet pack src/Tempo.Blazor/ -c Release -o ./packages
dotnet pack src/Tempo.Blazor.FluentValidation/ -c Release -o ./packages
```

### Run Demo Applications
```bash
# Start Demo API (terminal 1)
cd src/Tempo.Blazor.Demo.Api
dotnet run
# API runs on: https://localhost:5100

# Start Demo WASM (terminal 2)
cd src/Tempo.Blazor.Demo
dotnet run
# App runs on: https://localhost:7106

# Start Demo Server (terminal 2)
cd src/Tempo.Blazor.Demo.Server
dotnet run
# App runs on: https://localhost:7107
```

## Code Organization

### Component Categories (29 folders in `src/Tempo.Blazor/Components/`)

| Category | Components |
|----------|------------|
| Activity | `TmActivityLog`, `TmActivityComments`, `TmActivityAttachments`, `TmActivityTimeline`, `TmRichEditorFull`, `TmRichEditorSimple` |
| AITools | `TmAIPrompt` |
| Chat | `TmChat` |
| Avatars | `TmAvatar`, `TmAvatarGroup` |
| Buttons | `TmButton`, `TmSplitButton`, `TmCopyButton` |
| Charts | `TmChart` (Bar, Line, Pie, Donut, HorizontalBar — pure SVG), `TmStockChart` (Candlestick, OHLC, Line), `TmSparkline` (Line, Bar, Area, Pie), `TmGauge` (Arc, Circular, Linear) |
| Dashboard | `TmDashboard`, `TmWidgetSelector` (drag & resize grid, JS interop) |
| DataDisplay | `TmBadge`, `TmCard`, `TmEmptyState`, `TmMultiViewList`, `TmStatCard`, `TmAccordion`, `TmAccordionItem`, `TmChip`, `TmChipGroup`, `TmKanbanBoard`, `TmChangeDiff` |
| DataTable | `TmDataTable`, `TmDataTableColumn`, `TmColumnFilter`, `TmColumnPicker`, `TmPagination`, `TmViewManager`, `TmBulkActionBar` |
| Spreadsheet | `TmSpreadsheet`, `TmSpreadsheetGrid`, `TmSpreadsheetToolbar`, `TmSpreadsheetFormulaBar`, `TmSpreadsheetSheetTabs` (Excel-like with formulas, styling, XLSX import/export, freeze panes, merge cells) |
| Dropdowns | `TmDropdown`, `TmDropdownItem`, `TmFilterableDropdown` |
| Feedback | `TmNotificationBell`, `TmSkeleton`, `TmSpinner`, `TmAlert`, `TmDialog`, `TmModal`, `TmProgressBar`, `TmToastContainer`, `TmTooltip`, `TmPopover` |
| Files | `TmAttachmentManager`, `TmFileDropZone`, `TmPdfViewer` (PDF.js v5 powered viewer with thumbnails, search, text layer, rotation, continuous scroll) |
| Filters | `TmFilterBuilder`, `TmFilterChip` |
| Forms | `TmFormField`, `TmFormRow`, `TmFormSection`, `TmValidationSummary`, `TmValidatedField`, `TmDynamicFormRenderer`, `TmFormValidationMessage`, `TmInlineEdit` |
| Gallery | `TmImageGallery`, `TmLightbox` |
| Icons | `TmIcon`, `IconRegistry`, `IIconProvider`, `IconNames` |
| ImportExport | `TmImportWizard`, `TmImportPreview`, `TmExportOptions` |
| Inputs | `TmTextInput`, `TmTextArea`, `TmSelect`, `TmCheckbox`, `TmToggle`, `TmRadio`, `TmRadioGroup`, `TmSearchInput`, `TmPasswordStrengthIndicator`, `TmNumberInput`, `TmEntityPicker`, `TmExpressionEditor`, `TmMultiSelect` |
| Layout | `TmSidebar`, `TmBreadcrumbs`, `TmTopBar`, `TmCommandPalette`, `TmDrawer`, `TmSection`, `TmKeyboardShortcutsHelp`, `TmDockManager`, `TmDockPane` |
| Navigation | `TmTabs`, `TmTabPanel`, `TmContextMenu`, `TmContextMenuItem` |
| Notifications | `TmNotificationBell` (extended, per-item read, severity) |
| Pickers | `TmDatePicker`, `TmDateRangePicker`, `TmDateTimePicker`, `TmDateTimeRangePicker`, `TmTimePicker`, `TmTimeRangePicker`, `TmCalendarView` |
| Scheduler | `TmScheduler` with multiple views (Month, Week, Day, Timeline, Agenda) |
| Tags | `TmTagPicker` |
| Timeline | `TmTimeline` |
| Toolbar | `TmToolbar`, `TmToolbarButton`, `TmToolbarDivider` |
| TreeView | `TmTreeView` |
| Workflow | `TmStepper`, `TmWorkflowDesignerCanvas`, `TmWorkflowToolbox`, `TmWorkflowPropertiesPanel`, `TmWorkflowMinimap` |

### CSS Architecture

```
wwwroot/css/
├── tempo-blazor.css          # Main entry point with @imports
├── tempo-blazor.bundled.css  # Tracked source: the committed bundle of the above
├── tokens.css                # Design tokens (colors, spacing, typography)
├── tokens-dark.css           # Dark mode token overrides
├── base.css                  # Reset and base styles
├── animations.css            # Keyframes and animation utilities
├── breakpoints.css           # Responsive breakpoints
└── components/               # Individual component styles (90+ files)
    ├── _button.css
    ├── _input.css
    ├── _data-table.css
    └── ...
```

### Abstractions (Shared Library)

`Tempo.Blazor.Abstractions` contains zero-UI dependencies:
- **Interfaces**: `IDataTableDataProvider`, `IDropdownDataProvider`, `IFileAttachmentProvider`, `ITmLocalizer`, etc.
- **Models**: `SelectOption`, `DropdownItem`, `DataTableView`, `PagedResult`, `FilterDefinition`, etc.

This allows API/backend projects to reference these contracts without pulling Blazor dependencies.

## Development Conventions

### TDD Workflow
1. **RED**: Write bUnit test first
2. **GREEN**: Implement component to make test pass
3. **REFACTOR**: Clean up while keeping tests green

### Component Guidelines

#### Parameter Attributes
Every `[Parameter]` must have an XML documentation comment:
```csharp
/// <summary>Visual style variant. Defaults to Primary.</summary>
[Parameter] public ButtonVariant Variant { get; set; } = ButtonVariant.Primary;
```

#### No Hardcoded Text
All user-visible strings must use localization via `ITmLocalizer`:
```razor
<!-- GOOD -->
<button aria-label="@Loc["TmButton_AriaLabel"]">@Loc["TmButton_Text"]</button>

<!-- BAD -->
<button aria-label="Click me">Click me</button>
```

#### CSS Custom Properties
No hardcoded colors/sizes in CSS. Always use tokens:
```css
/* GOOD */
.tm-btn {
    background: var(--tm-color-primary);
    padding: var(--tm-space-2) var(--tm-space-4);
}

/* BAD */
.tm-btn {
    background: #3b82f6;
    padding: 8px 16px;
}
```

### Global Usings
All components have access to `ITmLocalizer` via `_Imports.razor`:
```razor
@inject ITmLocalizer Loc
```

### Component Naming
- **Prefix**: `Tm` (Tempo)
- **Format**: `Tm{ComponentName}.razor`
- **Namespace**: `Tempo.Blazor.Components.{Category}`

## Testing Strategy

### Unit Tests (bUnit)
**Location**: `tests/Tempo.Blazor.Tests/`

Test organization mirrors component structure:
```
Tests/
├── Components/
│   ├── Buttons/TmButtonTests.cs
│   ├── Inputs/TmTextInputTests.cs
│   └── ...
├── Localization/
│   ├── LocalizationTestBase.cs
│   └── TmButtonLocalizationTests.cs
└── Theme/ThemeServiceTests.cs
```

**Test base class** provides mocked localization:
```csharp
public class LocalizationTestBase : TestContext
{
    protected LocalizationTestBase()
    {
        Services.AddSingleton<ITmLocalizer>(new MockTmLocalizer());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
```

### E2E Tests (Playwright)
**Location**: `tests/Tempo.Blazor.E2E/`

Uses MSTest runner (`EnableMSTestRunner=true`). Tests run against running Demo applications:
- WASM: `https://localhost:7106`
- Server: `https://localhost:7107`
- InteractiveAuto: `https://localhost:7108`

Base classes provided:
- `WasmTestBase` – for WASM demo tests
- `ServerTestBase` – for Server demo tests
- `InteractiveAutoTestBase` – for InteractiveAuto demo tests

### API Tests
**Location**: `tests/Tempo.Blazor.Demo.Api.Tests/`

Uses `Microsoft.AspNetCore.Mvc.Testing` for integration testing.

## Localization

### Resource Files
**Location**: `src/Tempo.Blazor/Resources/` — three embedded JSON files (flat `"Key": "Value"` maps):
- `TmResources.json` – English (default)
- `TmResources.cs.json` – Czech
- `TmResources.fr.json` – French

> Note: these replaced the old `.resx` files; there are no `.resx` resources anymore.

### Adding New Keys
1. Add the key to `TmResources.json` (English)
2. Add the key to `TmResources.cs.json` (Czech)
3. Add the key to `TmResources.fr.json` (French)
4. Use in a component: `@Loc["KeyName"]` (`ITmLocalizer Loc` is injected globally via `_Imports.razor`)
5. Add the key to `MockTmLocalizer` in `tests/Tempo.Blazor.Tests/Localization/LocalizationTestBase.cs` (English map in `BuildEnglishLocalizer`, Czech map in `BuildCzechLocalizer`) so component tests resolve it

### Consuming Application Setup
```csharp
// Program.cs
builder.Services.AddTempoBlazor();

// Optional: Override with custom localizer
builder.Services.AddSingleton<ITmLocalizer, MyCustomLocalizer>();

// Set culture
var culture = new CultureInfo("cs");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
```

## Theming

### Using the Design System
Add CSS to `index.html`:
```html
<link href="_content/Tempo.Blazor/css/tempo-blazor.css" rel="stylesheet" />
```

### Theme Service
```csharp
// ThemeService is automatically registered by AddTempoBlazor()

// Component
@inject ThemeService ThemeService

<div data-theme="@ThemeService.ThemeName">
    <button @onclick="ThemeService.Toggle">Toggle Theme</button>
</div>
```

### Customizing Tokens
Override in app's CSS:
```css
:root {
    --tm-color-primary: #your-brand-color;
    --tm-font-sans: 'Your Font', sans-serif;
}
```

## FluentValidation Integration

### Setup
```bash
dotnet add package Tempo.Blazor.FluentValidation
```

```csharp
// Program.cs
builder.Services.AddTempoFluentValidation(typeof(MyValidator).Assembly);
```

### Usage
```razor
<EditForm Model="model" OnValidSubmit="Submit">
    <FluentValidationValidator />
    
    <TmFormField Label="Name">
        <TmTextInput @bind-Value="model.Name" />
        <ValidationMessage For="() => model.Name" />
    </TmFormField>
</EditForm>
```

## Custom Icons

Register custom icons in `Program.cs`:
```csharp
// Inline SVG
IconRegistry.Register("my-logo", "<path d='...'/><circle .../>");

// Or custom provider
IconRegistry.RegisterProvider(new MyFontIconProvider());
```

Use in components:
```razor
<TmIcon Name="my-logo" />
```

## TmPdfViewer Features

The `TmPdfViewer` component is a standalone PDF viewer powered by **PDF.js v5** (ES modules). It supports both single-page and continuous scroll viewing modes with a rich toolbar and advanced productivity features.

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Url` | `string?` | `null` | PDF document URL (required) |
| `Page` / `PageChanged` | `int` / `EventCallback<int>` | `1` | Two-way bound current page |
| `Scale` / `ScaleChanged` | `double` / `EventCallback<double>` | `1.0` | Two-way bound zoom scale |
| `ViewMode` / `ViewModeChanged` | `PdfViewMode` / `EventCallback<PdfViewMode>` | `SinglePage` | SinglePage or Continuous |
| `ShowToolbar` | `bool` | `true` | Show navigation toolbar |
| `ShowThumbnails` | `bool` | `false` | Show thumbnails sidebar |
| `ShowSearch` | `bool` | `false` | Show search toggle in toolbar |
| `ShowTextLayer` | `bool` | `false` | Render selectable text over canvas |
| `ShowViewModeToggle` | `bool` | `false` | Show single/continuous view mode buttons |
| `AllowRotation` | `bool` | `false` | Show rotation button |
| `AllowDownload` | `bool` | `true` | Show download link |
| `Height` | `string?` | `"600px"` | Viewer height CSS value |
| `Class` | `string?` | `null` | Additional CSS classes |

### Architecture

- **JS wrapper** (`pdf-viewer.js`): Standalone `window.tmPdfViewer` module that dynamically imports PDF.js v5, manages the PDF document lifecycle, and exposes methods for rendering, zooming, rotating, searching, thumbnails, and continuous scroll.
- **Composition pattern**: `TmNotionPdfBlock` delegates all PDF rendering to `TmPdfViewer` via child component parameters. The Notion block only owns caption editing, upload dialog, resize handle, and focus handling.
- **Dispatcher safety**: All `[JSInvokable]` methods wrap `StateHasChanged()` in `InvokeAsync(() => StateHasChanged())` to avoid Blazor dispatcher errors during JS interop callbacks.

### View Modes

- **SinglePage** (default): Renders one page at a time on a `<canvas>` with prev/next navigation.
- **Continuous**: Renders all pages in a scrollable container via `renderAllPages()`.

### Usage

```razor
<!-- Basic viewer -->
<TmPdfViewer Url="https://example.com/doc.pdf" />

<!-- Full-featured -->
<TmPdfViewer Url="@pdfUrl"
             ShowThumbnails="true"
             ShowSearch="true"
             ShowTextLayer="true"
             ShowViewModeToggle="true"
             AllowRotation="true"
             AllowDownload="true"
             Height="600px" />
```

---

## TmDiagramEditor Advanced Features

The diagram editor (`TmDiagramEditor`, `TmDiagramCanvas`) supports several advanced productivity features beyond basic drawing:

### Unified SVG Canvas Architecture (4-pane model)

Inspired by **draw.io / mxGraph**, the canvas uses a single SVG scene with four dedicated `<g>` panes instead of the legacy dual-layer SVG+HTML overlay. This eliminates the CTM/CSS-transform drift bug, enables true global Z-order interleaving between nodes and edges, and allows simple stencils to render as native SVG primitives.

| Pane | CSS Class | Purpose | Pointer Events |
|------|-----------|---------|----------------|
| **Background** | `.tm-diagram-bg-pane` | Grid, page background, page shadow, model-level group bounds | `none` |
| **Scene / Draw** | `.tm-diagram-scene-pane` | Nodes (native SVG shape + `<foreignObject>` label) and edges interleaved by `ZIndex` | `auto` |
| **Overlay** | `.tm-diagram-overlay-pane` | Selection outlines, drop-target highlights | `none` |
| **Decorator** | `.tm-diagram-decorator-pane` | Resize/rotate handles, connect arrows, edge waypoint handles | `auto` |

Key consequences:
- **Nodes** are rendered as `<g class="tm-diagram-node" transform="translate(...) rotate(...)">` containing a native SVG shape (`<rect>`, `<ellipse>`, `<polygon>`) for simple stencils and a `<foreignObject>` with rich HTML content for complex stencils.
- **Edges** live in the same scene pane and can be placed above or below nodes via `ZIndex`.
- **Selection outlines** are SVG `<rect>` elements injected into the overlay pane by `diagram-editor.js`.
- **Resize/rotate handles** are SVG `<rect>`/`<circle>` elements rendered by Blazor into the decorator pane and kept in sync during JS drags.
- Pan and zoom are driven exclusively by the SVG `viewBox`; there is no `_syncHtmlTransform` or HTML overlay CSS transform.

### Auto Layout (dagre)
- Integrated with `dagre.min.js` for automatic hierarchical layouts.
- `ApplyLayoutCommand` stores previous node positions for full undo/redo support.
- `RunLayoutAsync("TB" | "LR")` computes new positions via JS interop and applies them to the current selection.
- Toolbar dropdown **Layout** and property panel **Arrange → Auto Layout** expose the feature to users.

### Layers Panel
- `DiagramDocument` contains a list of `DiagramLayer` objects (`Id`, `Name`, `Order`, `IsVisible`, `IsLocked`).
- `TmDiagramLayersPanel` renders layer visibility toggles, lock toggles, active-layer radio buttons, inline rename, add/delete, and drag-and-drop reorder.
- `ReorderLayersCommand`, `ToggleLayerVisibilityCommand`, `ToggleLayerLockCommand`, `MoveNodesToLayerCommand` provide undoable operations.
- Canvas filters nodes via `IsLayerVisible` so hidden layers do not render.

### Format Painter
- `CopyStyleCommand` copies a node's `DiagramStyle` (fill, stroke, font, etc.) to a static `DiagramClipboard.Style`.
- `PasteStyleCommand` applies the copied style to any selection and supports undo.
- `CopyStyleCommand` with `includeSize: true` also records `W`/`H` into `DiagramClipboard`.
- `PasteSizeCommand` applies the recorded width/height to selected nodes and supports undo.
- Toolbar buttons and keyboard shortcuts `Ctrl+Shift+C` / `Ctrl+Shift+V` trigger the commands.

### Collapsible Nodes
- `DiagramNode` exposes `IsCollapsible`, `Collapsed`, and `ExpandedHeight`.
- `ToggleCollapseCommand` collapses a node to a header height (preserving original height for undo) and hides child nodes / non-top-bottom ports.
- Built-in stencils such as UML package, UML frame, org-chart manager, BPMN pool, and BPMN subprocess are marked `IsCollapsible = true`.

### Diagram Search
- `DiagramSearchService` performs free-text search across node `Id`, `StencilId`, `Data` values, and edge `Label`.
- `TmDiagramSearchPanel` provides a floating search bar with prev/next navigation.
- `Ctrl+F` is handled in `diagram-editor.js` to open the panel.
- Active results are highlighted with a pulsing `tm-diagram-search-match` CSS class and the canvas auto-centers on the match.

### SQL → ER Diagram Import
- `SqlParser` extracts tables, columns, primary keys, and foreign keys from SQL DDL (`CREATE TABLE`) using regex-based parsing.
- `SqlToErDiagramGenerator` converts parsed tables into `erd.entity` nodes and `DiagramEdge` relationships.
- `TmDiagramSqlImportDialog` provides a modal UI for pasting SQL, selecting layout direction (TB/LR), and previewing table/relation counts.
- Imported diagrams are created as new pages with A4 landscape size and automatically run through the dagre layout.

### CSV → Diagram Import
- `CsvParser` auto-detects delimiter (`,`, `;`, `\t`) and parses headers/rows using `CsvHelper`.
- `CsvToOrgChartGenerator`, `CsvToFlowchartGenerator`, and `CsvToTimelineGenerator` create diagrams from mapped columns.
- `TmDiagramCsvImportDialog` provides a modal UI for pasting CSV, selecting diagram type (Org Chart / Flowchart / Timeline), mapping columns, and previewing data rows.
- Org Chart uses `tree` layout, Flowchart uses `dagre`, and Timeline pre-positions nodes horizontally without auto-layout.

### Edge Rendering & Interaction
- **Cubic Bézier** — `DiagramEdge.CubicBezier` toggles between quadratic (`Q`) and cubic (`C`) SVG path commands for `Routing = "curved"`.
- **Isometric Routing** — `Routing = "isometric"` generates 30°/60°/90°/120°/150° angled segments via the JS orthogonal router.
- **Entity Relation Routing** — `Routing = "entityrelation"` produces horizontal tree-style arms (30px branches from source/target).
- **Block Arrow Shapes** — `Shape = "blockArrow"`, `"doubleArrow"`, `"flexArrow"` render edges as filled SVG polygons with constant or tapered shaft width.
- **Cardinality Labels** — `SourceCardinality` and `TargetCardinality` render as `<text>` elements positioned perpendicular to the edge terminal (~15px offset).
- **Rubber-band Edge Selection** — The JS rubber-band rectangle samples points along each edge path; intersecting edges are added to the selection.
- **Edge Selection Outline** — Selected edges render a dashed `tm-diagram-edge-path--selected-outline` path behind the visible stroke.
- **Virtual Bend Transaction** — Clicking an edge segment to insert a virtual bend + dragging it is captured as a single `DiagramCommandTransaction` (one undo/redo step). Escape during the drag rolls back the transaction.

## JavaScript Interop

Core JS files in `src/Tempo.Blazor/wwwroot/js/` require `<script>` tags in host pages when using the related core components:
- `dashboard.js` — required by `TmDashboard` (drag & resize grid)
- `workflow-designer.js` — required by `TmWorkflowDesignerCanvas` (SVG drag, pan, zoom, transition creation)
- `richEditor.js` — required by `TmRichEditorFull` / `TmRichEditorSimple` (contenteditable interop)
- `scheduler.js` — required by `TmScheduler` (drag & drop events)
- `signature-capture.js` — required by `TmSignatureCapture`

Split package assets live under their own static-web-asset base paths:
- `Tempo.Blazor.PdfViewer`: `_content/Tempo.Blazor.PdfViewer/js/pdf-viewer.js`
- `Tempo.Blazor.DiagramEditor`: `_content/Tempo.Blazor.DiagramEditor/js/dagre.min.js` and `_content/Tempo.Blazor.DiagramEditor/js/diagram-editor.js`
- `Tempo.Blazor.Wireframe`: `_content/Tempo.Blazor.Wireframe/js/wireframe-designer.js`
- `Tempo.Blazor.Spreadsheet`: `_content/Tempo.Blazor.Spreadsheet/js/spreadsheet.js` and `_content/Tempo.Blazor.Spreadsheet/js/spreadsheet-canvas.js`
- `Tempo.Blazor.NotionEditor`: `_content/Tempo.Blazor.NotionEditor/js/notion-editor.js`
- `Tempo.Blazor.Signing`: `_content/Tempo.Blazor.Signing/js/pdf-template-designer.js` (loaded by `TmPdfTemplateDesigner` as an ES module)

## Security Considerations

1. **XSS Prevention**: Components use `@` (encoded) output by default. Use `@((MarkupString)…)` only for trusted content.
2. **Icon SVGs**: Custom icons are rendered as `MarkupString`. Ensure SVG content is trusted/sanitized.
3. **No Secrets**: Demo API uses mock data stores with generated fake data.

## CI/CD Pipeline

**GitHub Actions**: `.github/workflows/publish-nuget.yml`

- **Triggers**: Push to main/master, tags (v*), pull requests, manual dispatch
- **Build Matrix**: .NET 8.0, 9.0, 10.0
- **Tests**: All tests must pass before publish
- **Packages**: Published to GitHub Packages
- **Versions**:
  - Tags: `v1.2.3` → version `1.2.3`
  - Manual (no suffix): `1.0.0`
  - Manual (with suffix): `1.0.0-beta1`
  - CI builds: `1.0.0-ci-{timestamp}`

## Useful Commands Reference

```bash
# Restore packages
dotnet restore

# Watch mode for development
dotnet watch --project src/Tempo.Blazor.Demo

# Clean build artifacts
dotnet clean

# Format code
dotnet format

# List NuGet package references
dotnet list package

# Check for outdated packages
dotnet list package --outdated

# Run specific test
dotnet test --filter "FullyQualifiedName~TmButtonTests"

# Generate coverage report
dotnet test --collect:"XPlat Code Coverage"
```

## Language Note

- **Code, XML documentation, and comments**: English
- **Planning documents** (in `planning/`): Czech
- **Library localization**: English (`en`) and Czech (`cs`) built-in, extensible

## NuGet Packages

| Package | Description |
|---------|-------------|
| `Tempo.Blazor` | Core component library, services, tokens, base CSS, and lightweight core JS |
| `Tempo.Blazor.All` | Compatibility metapackage that references core plus split feature packages, including Signing |
| `Tempo.Blazor.Abstractions` | Interfaces and models, zero UI dependencies |
| `Tempo.Blazor.PdfViewer` | PDF.js powered `TmPdfViewer` |
| `Tempo.Blazor.Codes` | QR code and barcode components |
| `Tempo.Blazor.DiagramEditor` | Diagram editor components, services, CSS, and JS |
| `Tempo.Blazor.Wireframe` | Wireframe editor components, CSS, and JS |
| `Tempo.Blazor.Modeling` | Modeling editor built on DiagramEditor |
| `Tempo.Blazor.Spreadsheet` | Spreadsheet editor components, XLSX support, CSS, and JS |
| `Tempo.Blazor.GanttXlsx` | Optional Gantt XLSX import/export helpers |
| `Tempo.Blazor.DocumentEditor` | Document editor UI and canvas runtime |
| `Tempo.Blazor.NotionEditor` | Notion-style editor and block/database UI |
| `Tempo.Blazor.Signing` | Signing workflows, document page overlays, PDF template designer, CSS, and JS |
| `Tempo.Blazor.FluentValidation` | FluentValidation integration for EditForm |

---

*This file is intended for AI coding agents. For human-readable documentation, see `README.md`.*
