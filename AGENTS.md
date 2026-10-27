# Tempo.Blazor – AI Agent Guide

## Project Overview

**Tempo.Blazor** is a comprehensive Blazor component library with 126+ reusable Razor components designed for AI-assisted development. The library provides a complete UI toolkit for building modern Blazor applications with support for multiple render modes (WebAssembly, Server, InteractiveAuto), localization, theming (light/dark), FluentValidation integration, and a CSS design system based on custom properties.

### Key Features
- **125+ reusable Razor components** organized into 28 categories (inputs, data tables, pickers, layout, feedback, charts, dashboards, workflow designer, etc.)
- **Multi-target .NET support**: .NET 8.0, 9.0, and 10.0
- **Full localization support** via `ITmLocalizer` (English, Czech, Slovak and French built-in, extensible)
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
scripts/run-e2e-full.ps1    # full lane: entire suite (~1450 tests, ~4h sharded)

# JS engine unit tests (node --test via scripts/run-node-tests.mjs; explicit file enumeration, no globs)
npm ci                         # once: installs esbuild for the document-editor bundle tests
npm run test:document-editor
npm run test:reporting-modules
npm run test:overlay           # core: pointer-drag, pointer-gesture, overlay, workflow/scheduler/dashboard pointer
npm run test:diagram-editor    # DiagramEditor: diagram-editor-touch.js + engine tests
npm run test:email-templates   # EmailTemplates: tm-email-save-shortcut.js (Ctrl/⌘+S guard)
npm run test:pdf-viewer        # PdfViewer: pdf-viewer-size.mjs + pdf-viewer-render.mjs (RenderTask bookkeeping)
npm run test:wireframe         # Wireframe: wireframe-designer.js pointer/export tests + wireframe-gestures.mjs
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

# Start InteractiveAuto demo (terminal 3)
cd src/Tempo.Blazor.Demo.InteractiveAuto
dotnet run
# App runs on: https://localhost:7108
```

E2E tests self-host all four demo apps by default (PlaywrightTestBase starts
API/WASM/Server/InteractiveAuto on demand). To run them against externally
managed hosts instead, start the four apps above and set
`TM_E2E_SELF_HOST=false`.

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
| Scheduler | `TmScheduler` (Month, Week, Day, Timeline, Agenda). `LayoutMode` classifies its own container; `MobileActionBar` is opt-in (`None` default). Side panel and event detail are host slots through `TmSidePanel`. Contract: [docs/scheduler.md](docs/scheduler.md) |
| Tags | `TmTagPicker` |
| Timeline | `TmTimeline` |
| Toolbar | `TmToolbar`, `TmToolbarButton`, `TmToolbarFileButton`, `TmToolbarDivider` |
| TreeView | `TmTreeView` |
| Workflow | `TmStepper`, `TmWorkflowDesignerCanvas`, `TmWorkflowToolbox`, `TmWorkflowPropertiesPanel`, `TmWorkflowMinimap` |

Components of the split packages live in their own projects rather than in this folder —
`Tempo.Blazor.EmailTemplates` (`TmEmailTemplateEditor`, `TmEmailTemplatePreview`, `TmEmailPropertyPanel`,
`TmEmailToolbox`, `TmEmailTemplateCanvas`, `TmEmailCanvasBlock`, `TmEmailImportDialog`,
`TmEmailExportDialog`, `TmEmailValidationPanel`, `TmEmailItemDialog`, `TmEmailObjectFields` and the
sub-editors), `Tempo.Blazor.DiagramEditor`, `Tempo.Blazor.Spreadsheet`, `Tempo.Blazor.Signing`, and so on.

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

#### Keyboard Activation
Exactly one activation per key press: never emulate Enter/Space on native activatable elements, always emulate on non-native focusables, and fence native children inside emulating containers with `@onkeydown:stopPropagation`. See [docs/keyboard-activation-convention.md](docs/keyboard-activation-convention.md).

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
**Location**: `src/Tempo.Blazor/Resources/` — four embedded JSON files (flat `"Key": "Value"` maps):
- `TmResources.json` – English (default)
- `TmResources.cs.json` – Czech
- `TmResources.sk.json` – Slovak
- `TmResources.fr.json` – French

> Note: these replaced the old `.resx` files; there are no `.resx` resources anymore.

### Adding New Keys
1. Add the key to `TmResources.json` (English)
2. Add the key to `TmResources.cs.json` (Czech)
3. Add the key to `TmResources.sk.json` (Slovak)
4. Add the key to `TmResources.fr.json` (French)
5. Use in a component: `@Loc["KeyName"]` (`ITmLocalizer Loc` is injected globally via `_Imports.razor`)
6. Add the key to `MockTmLocalizer` in `tests/Tempo.Blazor.Tests/Localization/LocalizationTestBase.cs` (English map in `BuildEnglishLocalizer`, Czech map in `BuildCzechLocalizer`) so component tests resolve it

`TmResourceCompletenessTests` (same test folder) guards key completeness: every key in the English
table must exist in the Czech, Slovak and French tables, with identical `{0}`-style placeholders,
and every plural family must provide the forms each language's grammar needs (see
[docs/localization-plurals.md](docs/localization-plurals.md)).

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

`Tempo.Blazor.PdfViewer` is a split package (static-asset base `_content/Tempo.Blazor.PdfViewer/`)
shipping a PDF.js v5 powered viewer, an annotator and the shared comment-thread cards. Full
contract — fit modes, header/slots, toolbar DOM, responsive behavior, touch and the migration
notes — lives in [docs/pdf-viewer.md](docs/pdf-viewer.md); the demo pages are `/pdf-viewer`,
`/pdf-annotator` and `/redaction`.

### Components

| Component | Purpose |
|-----------|---------|
| `TmPdfViewer` | The viewer: PDF.js rendering, toolbar, thumbnails, search, text layer, rotation, continuous scroll, optional annotations panel |
| `TmPdfAnnotator` | The annotator: `TmPdfViewer` + annotation tools on one merged toolbar (`TmEditorShell` layout) |
| `TmPdfAnnotationThreadPanel` | The viewer's comments panel (docked panel / side sheet / bottom sheet) |
| `TmPdfAnnotatorPanel` | The annotator's thread panel (same cards, annotator chrome) |
| `TmPdfThreadList` / `TmPdfThreadCard` / `TmPdfCommentItem` / `TmPdfComposer` | Shared thread cards used by both panels (PD4) |
| `TmPdfAnnotationLayer` / `TmPdfAnnotatorLayer` | Page-overlay marker layers |
| `TmRedactionLayer` | Redaction stage; keeps its viewer pinned to `SinglePage` |

### TmPdfViewer Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Url` | `string?` | `null` | PDF document URL (required) |
| `Page` / `PageChanged` | `int` / `EventCallback<int>` | `1` | Two-way bound current page (1-based; scroll-tracked in Continuous) |
| `Scale` / `ScaleChanged` | `double` / `EventCallback<double>` | `1.0` | Two-way bound zoom scale |
| `ViewMode` / `ViewModeChanged` | `PdfViewMode` / `EventCallback<PdfViewMode>` | `SinglePage` | `SinglePage` or `Continuous` |
| `ZoomMode` / `ZoomModeChanged` | `PdfZoomMode` / `EventCallback<PdfZoomMode>` | `Auto` | `Auto` (fit-width on mobile, bound scale elsewhere), `Custom`, `FitWidth`, `FitPage`; any zoom button/preset/pinch demotes to `Custom` |
| `LayoutMode` | `TmLayoutMode` | `Auto` | Responsive layout; a forced value (or a forced cascading `TmLayoutContext` ancestor) pins Desktop/Tablet/Mobile |
| `ToolbarLabels` | `ToolbarLabels` | `Icons` | `Icons` (icon-only), `IconsWithText`, or `Auto` (labels except on a narrow toolbar; labels render below icons on tablet) |
| `ShowToolbar` | `bool` | `true` | Show the toolbar row |
| `ShowThumbnails` | `bool` | `false` | Show the thumbnails sidebar |
| `ShowSearch` | `bool` | `false` | Show the search group / search bar |
| `ShowTextLayer` | `bool` | `false` | Render a selectable text layer over the canvas |
| `ShowViewModeToggle` | `bool` | `false` | Show the single/continuous segmented control |
| `ShowMobileActionBar` | `bool` | `true` | Render the mobile `TmMobileActionBar` (Pages \| Search \| Comments); hosts folding these entries into their own bar pass `false` and use `OpenThumbnailsSheetAsync()` / `OpenSearchAsync()` |
| `AllowRotation` | `bool` | `false` | Show the rotation button (both directions, icon-only on desktop/tablet with the localized text in tooltip + aria-label) |
| `AllowDownload` | `bool` | `true` | Show the Download action (primary; moves into the header with `ShowHeader`) |
| `ShowOpenLink` | `bool` | `true` | Show the "Open in new tab" link |
| `SearchPresentation` | `PdfSearchPresentation` | `Floating` | Desktop/tablet search presentation: `Floating` (card anchored over the body; Escape restores the toolbar focus) or `Inline` (the pre-2.9.1 in-flow bar); mobile always keeps the in-flow bar |
| `ShowFileSize` | `bool` | `true` | Append the formatted file size to the default header subtitle (with `ShowHeader`) |
| `FileSize` | `long?` | `null` | Host-supplied byte size; wins over the PDF.js-reported length (`getDownloadInfo`) |
| `ShowHeader` | `bool` | `false` | Show the document header above the toolbar |
| `Title` / `Subtitle` | `string?` | `null` | Header text; the subtitle defaults to a neutral page count ("Pages: N") |
| `ThumbnailsCollapsed` / `ThumbnailsCollapsedChanged` | `bool` / `EventCallback<bool>` | `false` | Two-way collapsed thumbnails (toolbar "Pages" toggle + sidebar button drive it) |
| `Height` | `string?` | `"600px"` | Viewer height (CSS value; `100%`/`100dvh` resolve through the flex chain) |
| `Class` | `string?` | `null` | Additional CSS classes |
| `EnableAnnotations` | `bool` | `false` | Enable the annotation layer and comments panel (markers render per page in both view modes; FZE-13) |
| `AnnotationProvider` | `IPdfAnnotationProvider?` | `null` | Persistence; in-memory provider when omitted |
| `DocumentId` | `string?` | `null` | Provider document id (falls back to `Url`) |
| `CurrentUser` | `DocumentCommentUser?` | `null` | Author of annotations created in this viewer |
| `ShowResolvedAnnotations` | `bool` | `false` | Show resolved threads by default |
| `Annotations` / `AnnotationsChanged` | `IReadOnlyList<DocumentCommentThread>?` / `EventCallback<...>` | `null` | Seed threads when no provider is supplied |
| `OnTextSelected` | `EventCallback<PdfTextSelection>` | — | Text selection in the text layer (`EnableTextSelectionEvents` without annotations) |
| `EnableTextSelectionEvents` | `bool` | `false` | Raise `OnTextSelected` without enabling the annotation layer |
| `OverlayContent` | `RenderFragment?` | `null` | Overlay aligned with the current page canvas (single-page only) |
| `PageOverlayContent` | `RenderFragment<PdfPageOverlayContext>?` | `null` | Templated per-page overlay (context: `PageNumber`, `IsCurrentPage`, `Width`, `Height`, `Rotation`); every mounted page in Continuous, the current page in SinglePage (FZE-13) |
| `OnDocumentLoaded` | `EventCallback<int>` | — | Total page count once loaded |
| `ToolbarStart` / `ToolbarEnd` | `RenderFragment?` | `null` | Host groups composed into the toolbar (the annotator injects its tools) |
| `HeaderStart` / `HeaderActions` | `RenderFragment?` | `null` | Header slots (used only with `ShowHeader`) |
| `SelectionCtaContent` | `RenderFragment?` | `null` | Content of the sticky in-flow selection CTA row (outside the scroll area) |

Public methods: `GoToPageAsync(int)`, `SetScaleAsync(double)`, `RotateAsync()`,
`RotateCounterClockwiseAsync()`, `NextMatchAsync()` / `PreviousMatchAsync()`,
`OpenThumbnailsSheetAsync()`, `OpenSearchAsync()`.

### Toolbar, layout and zoom

- **Toolbar** — the core `TmToolbar` (`Overflow=Menu` with priority-based "More"), root class
  `.tm-pdf-viewer__toolbar`, test id `pdf-viewer-toolbar`. Groups carry the stable
  `data-tm-group` values `navigation`, `zoom`, `rotate`, `view`, `panels`, `search`,
  `comments`, `export` (see [docs/toolbar.md](docs/toolbar.md)); hosts key off these, never
  positional selectors. The annotator injects an additional `history` group (its undo/redo
  pair) through `ToolbarStart`, rendered before the tool pills. The page is an editable
  `<input type="number">` (Enter/blur navigates,
  out-of-range reverts), the zoom label is a `TmDropdown` with presets 50/75/100/125/150/200 %
  plus Fit width / Fit page, and the comments toggle shows the open-thread count.
- **Layout modes** — resolved from the viewer's own container (`container-type: inline-size`):
  **desktop** keeps the docked chrome; **tablet** (640–1023 px) starts with the thumbnails
  collapsed, shows labels below icons and the comments panel as a `TmSidePanel` side sheet
  (closed by default); **mobile** (<640 px) gets a compact single-row toolbar with a "More"
  menu, an in-flow `TmMobileActionBar` (Pages | Search | Comments), bottom sheets for the
  thumbnails/comments/zoom/search and `ZoomMode=Auto` → fit-width. `LayoutMode` (or a forced
  cascading `TmLayoutContext` ancestor) pins a mode for tests and hosts; panel defaults apply
  per layout until the user touches the panels themselves.
- **Zoom modes** — `PdfZoomMode`: `Auto` (default; fit-width on a mobile layout, the bound
  `Scale` everywhere else), `Custom` (user/binding-driven — the pre-Auto behavior), `FitWidth`
  and `FitPage` (JavaScript computes the scale from the container and keeps recomputing it on
  resize via a ResizeObserver). A fit scale may fall outside the 50–200 % presets — the exact
  container ratio wins — and is clamped to 0.1–5 in `pdf-viewer.js`. Pinch-zoom commits into
  `Custom` too.
- **View modes** — `SinglePage` renders one canvas; `Continuous` renders a lazy scroll column:
  per-page placeholders with the real aspect ratio, IntersectionObserver + serial render queue
  (a 100+ page document never blocks the UI), canvases released outside the ±50 % viewport band
  (limit 12 painted), `OnVisiblePageChanged` updating `Page`/`PageChanged` + the active
  thumbnail, per-page text layer and search, and the scroll position preserved across zoom /
  rotation / view-mode switches. **Annotations in Continuous (FZE-13; decision
  `PDF-VIEWER-CONTINUOUS-ANNOTATIONS`)**: pages inside the ±50 % viewport band get a Blazor-owned
  overlay slot next to the JS-owned pages DOM (geometry reported via `OnContinuousPageGeometry`,
  so the lazy render and the 12-painted-page limit keep holding), markers render per mounted
  page, text selection resolves the anchor page and clamps cross-page selections, and selecting
  a thread scrolls to its anchor instead of switching the mode. `OverlayContent` stays
  single-page only; the templated `PageOverlayContent` works in both modes.

### TmPdfAnnotator

Built on `TmPdfViewer` + `TmEditorShell`. **One toolbar**: the tool pills (Browse / Highlight /
Comment / Stamp / Draw — `PdfAnnotatorMode`, two-way `Mode`), the color label
(`AnnotationColor`, per-author/role colors via `AuthorColors`/`RoleColors`) and the panel toggle
merge into the embedded viewer's toolbar through `ToolbarStart`/`ToolbarEnd`; the export group
("Export PDF | Export flattened | Print" — `AllowExport`, `ExportFileName`, `ExportAsync(flatten)`,
`PrintAsync()`) lives in the header row when `ShowHeader` is true. Pass-through parameters:
`ShowThumbnails`, `ShowSearch`, `SearchPresentation`, `AllowRotation`,
`ThumbnailsCollapsed` (two-way), `ZoomMode` (two-way), `ShowOpenLink`, `ToolbarLabels`,
`ViewMode`/`ViewModeChanged` (two-way, default `SinglePage` — additive, FZE-14) and
`ShowViewModeToggle` (default `false`, FZE-14). With
`ViewMode.Continuous` the annotator's layer and interaction surface render per page of the
scroll column through the viewer's `PageOverlayContent` slots — every tool anchors on the
slot's page, an ink stroke stays confined to its start page (clipped at the boundary), drafts
and composers stay bound to the draft page, and panel thread selection scrolls to the anchor
(the shared `TmPdfViewer.ScrollToAnchorAsync` path) without leaving Continuous; export/print
are view-mode independent. `TmRedactionLayer` keeps its viewer explicitly pinned to
`SinglePage` with no toggle. `ShowViewerToolbar` (default `true`)
is kept for compatibility: when `false`, the viewer chrome is hidden and the annotator renders a
compact tools row of its own. **Runtime color picker (FZE-12)**: `AllowColorPicker` (default
`true`) turns the color label into a trigger for a `TmColorPalette` popover (a bottom sheet on a
mobile layout); `ColorPalette` overrides the offered colors and `ColorNames` names the swatches.
The chip/palette display color is the picked color or the per-author/role fallback — selecting
the already-checked fallback swatch raises nothing, and the reset action is disabled while the
author color is shown. `AnnotationColorChanged` is two-way: a pick raises the color, the reset
raises `null`, and a new document resets the pick back to the parameter baseline.
Responsive (`LayoutMode`, default `Auto`): **desktop** keeps the
merged toolbar and the docked thread panel; **tablet** renders the tools as a full-width labelled
row and the panel as a docked aside; **mobile is G2 — viewing + light edits only**: the tool
palette offers exactly Browse and Add comment (highlight/stamp/draw are hidden, an active edit
mode falls back to Browse with a localized hint), the F5 `TmMobileActionBar` carries the export
entries and the thread panel is a non-modal bottom sheet (peek/half/full) that starts CLOSED
at load. `ShowPanel`/`ShowPanelChanged` two-way; the panel renders with `ShowChrome=false`
inside the sheet. Touch: one finger or pen draws, two fingers pinch/pan (the in-progress stroke
is cancelled on the second finger; `pen` has priority), and a tablet-only sticky "Highlight"
CTA surfaces a touch-selection draft. **Undo/redo (FZE-15)**: `AllowUndo` (default `true`)
records every annotation mutation on a command stack (`PdfAnnotationHistory`) and renders an
undo/redo pair as its own `data-tm-group="history"` group before the tool pills at every call
site (merged toolbar, tablet/compact row, mobile "More" entries); the buttons disable while a
step is in flight and carry `Ctrl+Z` / `Ctrl+Shift+Z`|`Ctrl+Y` via `aria-keyshortcuts`.
`UndoAsync()`/`RedoAsync()` with `CanUndo`/`CanRedo` are public, `OnHistoryChanged` fires on
every stack transition, and a visually-hidden live region announces the localized result
("Undo: {action}") plus the conflict hint. Undoing a delete re-creates the thread from its
snapshot with a fresh id remapped through the stack, so later steps keep targeting it.

### Architecture

- **JS wrapper** (`pdf-viewer.js`): `window.tmPdfViewer` ES module that dynamically imports
  PDF.js v5, keys document state PER ROOT ELEMENT (never per canvas), renders HiDPI with a DPR
  cap of 2, and splits fit/download/continuous/render logic into node-tested modules
  (`pdf-viewer-fit.mjs`, `pdf-viewer-download.mjs`, `pdf-viewer-continuous.mjs`,
  `pdf-viewer-size.mjs`, `pdf-viewer-render.mjs` — `npm run test:pdf-viewer`). Pinch-zoom comes
  from the core sibling `pointer-gesture.js` imported across the static-asset boundary — never a
  package-local copy (see [docs/touch-interaction.md](docs/touch-interaction.md)).
- **Composition pattern**: `TmNotionPdfBlock` delegates all PDF rendering to `TmPdfViewer` via
  child component parameters; the Notion block only owns caption editing, upload dialog, resize
  handle, and focus handling.
- **Dispatcher safety**: All `[JSInvokable]` methods wrap `StateHasChanged()` in
  `InvokeAsync(() => StateHasChanged())` to avoid Blazor dispatcher errors during JS interop.
- **Token contract**: the package reads canonical core tokens (`--tm-color-primary`,
  `--tm-border-color`, `--tm-radius-card`, `--tm-bg-workspace`, …) — never the removed undefined
  aliases. Component-local knobs live on each root: `--tm-pdf-page-shadow` and
  `--tm-pdf-panel-width` (viewer thumbnails 140px, annotator panel 300px, thread panel 280px).

### Usage

```razor
<!-- Basic viewer -->
<TmPdfViewer Url="https://example.com/doc.pdf" />

<!-- Full-featured, responsive (demo defaults) -->
<TmPdfViewer Url="@pdfUrl"
             ToolbarLabels="ToolbarLabels.Auto"
             ShowThumbnails="true"
             ShowSearch="true"
             ShowTextLayer="true"
             ShowViewModeToggle="true"
             AllowRotation="true"
             AllowDownload="true"
             ShowHeader="true"
             Title="Contract draft"
             Height="600px" />

<!-- Fit-width at every width (opt out of the Auto default) -->
<TmPdfViewer Url="@pdfUrl" ZoomMode="PdfZoomMode.FitWidth" />

<!-- Annotator with a header and export -->
<TmPdfAnnotator Url="@pdfUrl" ShowHeader="true" Title="@title" AllowExport="true" />
```

---

## TmWireframeEditor Chrome & TmDocumentOpenDialog

### Wireframe editor chrome (redesign plan ce19aa8c)

`TmWireframeEditor` renders through the core `TmEditorShell` and, like the DiagramEditor, resolves its presentation from its **own container** (`data-editor-layout` on the root, `LayoutMode="Auto"`, default breakpoints 768 / 1200; a CSS size container `tm-wd-editor`, never the viewport). Full contract: [docs/wireframe-editor.md](docs/wireframe-editor.md).

- **Desktop (≥1200):** header (document title, `HeaderStart` / `HeaderActions` host slots - there is no built-in Open or Save), ONE labelled `TmToolbar` command row (`Overflow=Menu`, "More" for the rest), a docked left panel with **Components | Pages | Layers** tabs (the Pages tab only with `PageNavigation=Panel`; the default `Tabs` keeps the horizontal page tabs), a docked inspector (empty selection = Canvas with device presets / Page / Grid; selection = Element with **Name**), and the minimap card.
- **Tablet (768–1199):** two toolbar rows, the left panel is an overlay drawer and the inspector a non-modal side sheet (at most one open; the core `TmEditorShell` `TabletPanelPresentation.Overlay`, test ids `wd-left-drawer` / `wd-inspector-sheet` kept), the minimap is hidden, 44 px targets on `(pointer: coarse)`; full editing with Pointer Events (drag, handles with `--tm-wd-handle-hit`, two-finger pinch / pan, 500 ms long-press context menu).
- **Mobile (<768) - viewing + light edits (decision G2):** compact header, a tool row, a `TmMobileActionBar` (Add | Pages & layers | Export | More) and ONE non-modal bottom sheet (peek / half / full, starts closed). Selecting an element peeks the Properties sheet with Duplicate / Lock / Delete; the context menu is the shared action sheet; export is a bottom sheet. Resize / rotate handles, rubber band, connectors, align / distribute, grouping, rulers, the minimap and JSON import stay tablet+ (`TmWireframeDesignerCanvas.LightEditing`).

**Stable toolbar contract (frozen):** groups carry `data-tm-group` from the frozen set `tools | history | arrange | group | align | snap | format | view | zoom | file` (additive growth only). Where a group renders (bar / "More" menu / second row) is a layout detail - hosts and tests key off `data-tm-group` and `data-testid`s (`wd-export`, `wd-toggle-left`, `wd-toggle-inspector`, `wd-left-drawer`, `wd-inspector-sheet`, …), never positional selectors on the toolbar DOM.

**`ReadOnly` contract:** `ReadOnly`, `ShowRulers`, `ShowGrid` and `GridSize` are plain `[Parameter]`s and are **never assigned inside the component**. The built-in Read-only / rulers / grid controls drive internal state and raise `ReadOnlyChanged` / `ShowRulersChanged` / `ShowGridChanged` / `GridSizeChanged` (`@bind-…`); the effective state is `IsReadOnly`. A host-pinned `ReadOnly="true"` without `ReadOnlyChanged` hides the toggle and blocks every editing path (canvas, inspector, layers, pages, context menu, toolbox, quick actions).

**Non-drag alternatives (WCAG 2.5.7):** every drag control has a tap / keyboard path - toolbox click / Enter / Space, arrow-key nudge and the inspector X / Y / W / H, layers and pages row menu + F2 + Alt+↑/↓, zoom buttons. A new drag control needs the same.

**Pointer events:** the canvas uses the shared `pointer-drag.js` / `pointer-gesture.js` (served from `_content/Tempo.Blazor/js/`) - never write a second drag or pinch implementation; the middle-button pan is the one documented exception. A gesture cancelled by Escape, `pointercancel` or a second finger rolls back and is announced. Node tests: `npm run test:wireframe`.

**Canvas render gate:** with `LightEditing` the canvas skips a parent render whose snapshot (document identity, page, page size, grid, snapping, read-only, scope, class) and `ContentVersion` are unchanged; the editor bumps `ContentVersion` on every command / undo / page switch. A standalone host must bump it after in-place edits. A new render-affecting `[Parameter]` belongs in `Snapshot()`.

### TmDocumentOpenDialog (core, shared)

`TmDocumentOpenDialog` (`src/Tempo.Blazor/Components/Files/`) is the shared "open a document" dialog of the Wireframe, Diagram and Spreadsheet Notion blocks (`ShowModeToggle=true` keeps the Link / Copy choice) and the demo `/document-open-dialog` page. The public API (`Provider`, `Kind`, `Open`, `OnSelected`, `ShowModeToggle`, `DefaultMode`, …), `DocumentOpenResult` and `ITempoDocumentLibraryProvider` are stable; new parameters are opt-in (`ShowRecent` adds a "Recent" node through `BrowseAsync`, `LayoutMode` pins desktop / tablet / mobile for tests). Library-wide queries (the Recent node, a search) state `DocumentLibraryQuery.Scope = DocumentLibraryScope.Library` (`Folder` default, `Subtree`, `Library`; a provider without support may degrade to `Folder`) instead of relying on `FolderPath = null`.

- **Desktop / tablet (dialog body ≥ 640 px):** XLarge modal, collapsible WAI-ARIA tree (`.tm-dod-tree-node`, roving tabindex), breadcrumb, sortable list / grid, always-visible disabled Rename / Delete, radio-card Link / Copy footer.
- **Mobile (< 640 px of dialog body):** a FULLSCREEN drill-down panel: no tree - `button.tm-dod-folder-row[data-path]` rows plus `.tm-dod-back`, two-line document rows with the `PreviewSvg` thumbnail and a per-row "⋯" menu, a `TmMobileActionBar` (`[data-action-id=new-folder|rename|delete]`) + Open in a sticky footer, compact Link / Copy cards, no `.tm-dod-cancel` (the header close cancels); double tap only selects. The layout is measured from the dialog's own container with a 112 px hysteresis while the panel is up (so it cannot flap); E2E that must work in both layouts uses `'.tm-dod-tree-node, .tm-dod-folder-row'`.
- Rules: scoped CSS reaches the `TmModal` body only through `.tm-dod-host ::deep …`; no new animated subject in core stylesheets (`ReducedMotionSelectorTests`); `.tm-dod-*` classes and all `--tm-*` tokens in `TmDocumentOpenDialog.razor.css` must exist (`TmDocumentOpenDialogCssTests`).

---

## TmDiagramEditor Advanced Features

The diagram editor (`TmDiagramEditor`, `TmDiagramCanvas`) supports several advanced productivity features beyond basic drawing:

### Editor Chrome (redesign plan 94d441f2)

Since the DiagramEditor redesign the editor renders through the core `TmEditorShell` and resolves
its presentation from its own container (`data-editor-layout` on the root, default breakpoints
768/1200): desktop (≥1200) shows the full chrome — header (page tabs, document title, ONE
Inspector toggle, "⋯" view-settings menu), one `TmToolbar` command row (`Overflow=Menu` with a
"More" overflow), docked toolbox / inspector, status bar (zoom + Read-only toggle) and a minimap
card;
tablet (768–1199) keeps the docked panels (at most one expanded, the other rails), touch gestures
and the **floating minimap overlay**; mobile (<768) is **viewing + light edits** (G2): a non-modal
bottom sheet (peek/half/full) holds the Shapes palette and the Properties | Layers inspector, a
Select/Shapes/Properties action bar (Pan in More) drives the surfaces, the sheet starts closed at
load, the minimap is opt-in via "⋯" (hidden at load only while that toggle exists), the Shapes
sheet keeps the General category first with the rest collapsed, and resize/rotate, edge drawing,
layout and import stay tablet+ (`TmDiagramCanvas.ShowTransformHandles` / `ShowConnectArrows` drop
the affordances). The mobile inspector presents flat Shapes | Properties | Layers sheet tabs (the
shell's one `Extra` sheet tab).

**Stable toolbar contract (frozen since phase 1):** groups carry `data-tm-group` from the frozen
set `history | clipboard | tools | format | insert | arrange | search | panels | zoom | view |
canvas-size | layout | export | templates | import | readonly`; `ToolbarPreset` (`Full`/`Viewer`)
and `ToolbarGroups` (flags, `null` = by preset) decide the group set; `HeaderStart`/`HeaderActions`
are host-action slots. Where a group renders (header / toolbar row / status bar / More menu) is a
layout detail that may change — hosts (Modeling) key off `data-tm-group`/presets, never positional
CSS selectors on the toolbar's internal DOM. `ReadOnly` is never assigned inside the component;
the built-in toggle drives only the internal user state surfaced as `IsReadOnly`, and a host-pinned
`ReadOnly="true"` hides the toggle and blocks every editing path. The inspector presents
"Properties | Layers" tabs (`InspectorLayout`, `Stacked` opt-in); the minimap chooses card vs
overlay through `MinimapPlacement`; pages stay as top tabs in every layout. Full contract:
[docs/diagram-editor.md](docs/diagram-editor.md).

**Touch:** all pointer work runs on the shared `pointer-drag.js` / `pointer-gesture.js` helpers
(imported by the package's `diagram-editor-touch.js`) — resize/rotate handles carry an invisible
hit area sized by `--tm-diagram-handle-hit` (`var(--tm-space-6)` = 24px,
`var(--tm-touch-target)` = 44px on coarse pointers), two fingers pinch/pan
the `viewBox`, long-press opens the context menu (mobile: the F3 bottom sheet; tablet/desktop: an
anchored finger-sized menu offset from the finger via `data-anchor="touch"`), and toolbox tiles
insert on tap while drag-and-drop keeps working.

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
- **Decorator pane paint order** follows the rule **buttons > gesture hit areas > visual handles**: each selected node renders, in order, (1) `g.tm-diagram-node-handles` (the visible resize/rotate handles plus their enlarged hit areas), (2) `g.tm-diagram-port-hits` (the finger-sized gesture hit circles over the ports) and (3) `g.tm-diagram-connect-arrows` (the quick-connect arrow buttons). Whatever a finger must reach paints last — a press lands on the topmost painted element, so the arrows stay reachable over the port hit discs on a coarse pointer. The JS transform sync (`_updateSelectionTransforms`, `_applyNodeRotation`) keeps every `[data-node-id]` group in the pane in sync generically, and a live resize hides `.tm-diagram-port-hits` and `.tm-diagram-connect-arrows` (their last-rendered positions would be stale while the geometry changes).
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

## Email Template Editor

The email editor is the 2.10.0 split into three packages, and the split is a **contract**, not an
implementation detail (decisions D11/D17 of `planning/tm-email-template-editor-2.10.md`):

```
Tempo.Blazor.EmailTemplates.Abstractions   model, DTOs, IEmailPreviewRenderer, IEmailTemplateStore,
                                           IEmailSender, save contract, TemplateSecurityOptions,
                                           URL/colour/CSS policy, JSON localization tables
        ↑                                  (no Blazor, no Scriban/Mjml.Net/AngleSharp)
Tempo.Blazor.EmailTemplates                editor UI + model half; no engine, so it runs in WASM
        ↑
Tempo.Blazor.EmailTemplates.Engine         Scriban sandbox, Mjml.Net compiler, AngleSharp sanitizer,
                                           render pipeline, LocalEmailPreviewRenderer
```

Rules that keep it that way:

- **The editor package must never reference the Engine.** `PackageBoundaryTests`
  (`tests/Tempo.Blazor.EmailTemplates.Engine.Tests`) walks the transitive `ProjectReference` closure of
  the editor, of the WASM demos, of `Tempo.ReportServer.Web.Client` and of `Tempo.Blazor.All`; there is
  **no exception any more** (D11 holds for the Report Server too). The scheduled-report e-mail is
  rendered by the server host (`Tempo.ReportServer.Web`: `AddReportServerEmailDelivery()` registers
  the Engine, `ReportEmailDeliveryService` and `ReportScheduleWorker`); `AddCommonServices` (it also runs
  in the WebAssembly leg) only registers the model/stores and a null-object `IReportScheduleRunner`
  (`CanRun = false`) that `AddReportServerEmailDelivery` replaces, so the demo "Run now" button is not offered in the browser
  (decision `REPORTSERVER-ENGINE-SERVER-ONLY`).
- **The Engine pins `Abstractions` to an exact version** (`[2.10.0]`) through the
  `PinAbstractionsToTheExactVersion` MSBuild target, because the two share `internal` types through
  `InternalsVisibleTo`. `PackageBoundaryTests.PackedEngineNuspec_PinsAbstractionsToTheExactVersion` packs
  the project and reads the `.nuspec`, so the pin cannot regress to a `>=` range.
- **Two registrations, never one:** `AddTempoEmailTemplateEditor()` (UI + model) and
  `AddTempoEmailTemplateEngine()` (rendering). `AddTempoEmailTemplates()` is `[Obsolete]` and registers
  no engine.
- **The preview and the HTML export go through `IEmailPreviewRenderer`** — the only seam a WASM host has
  to implement. A missing registration renders `EmailPreviewStatus.Unavailable`, never an empty iframe.
- **Saving is host-reported:** `OnSave` is `Func<EmailSaveRequest, Task<EmailSaveResult>>?`, there is no
  `OnAutoSave` (auto-save is `EmailSaveTrigger.AutoSave`), and `SaveAsync()` is public so a
  `TmNavigationGuard` can drive it.
- **Escaping is on by default** for the HTML/MJML body (subject/preheader/text stay raw); the only opt-out
  is `{{ value | html.trusted }}` and the result still passes the sanitizer. The URL allow-list replaces a
  refused scheme with `#` and audits `TemplateSecurityEventKind.BlockedUrlScheme`. Never weaken either —
  see the security rules in the plan.
- **Strings are embedded JSON** (`Resources/TmEmailResources[.cs|.sk|.fr].json`) resolved through a closed
  `IStringLocalizer<TmEmailResources>`; the package does not call `AddLocalization()`. A host replaces the
  wording through `ITmEmailLocalizer` / `IStringLocalizer<TmEmailResources>`.

Docs: [docs/email-templates/HOSTING.md](docs/email-templates/HOSTING.md) (registration, preview seam, save
contract, dirty guard, `ReadOnly`, `ItemEditingMode`, layout parameters, audit sink, limits),
[docs/email-templates/TEMPLATE_SYNTAX.md](docs/email-templates/TEMPLATE_SYNTAX.md) (Scriban, escaping, URL
policy, culture), [docs/email-templates/MIGRATION-2.9-to-2.10.md](docs/email-templates/MIGRATION-2.9-to-2.10.md)
(every breaking change with a before/after) and
[docs/email-templates/MJML_ATTRIBUTE_PARITY.md](docs/email-templates/MJML_ATTRIBUTE_PARITY.md).

## JavaScript Interop

Core JS files in `src/Tempo.Blazor/wwwroot/js/` require `<script>` tags in host pages when using the related core components:
- `dashboard.js` — required by `TmDashboard` (drag & resize grid; imports the shared `pointer-drag.js` from the same folder — no extra script tag)
- `workflow-designer.js` — required by `TmWorkflowDesignerCanvas` (SVG drag, pan, zoom, transition creation)
- `richEditor.js` — required by `TmRichEditorFull` / `TmRichEditorSimple` (contenteditable interop)
- `scheduler.js` — required by `TmScheduler` (classic script, `window.TempoBlazor.Scheduler`). It owns the scheduler maths only: slot snapping, the live size of the event, the resize commit and the roving scroll guard. The gesture itself comes from the shared helpers, loaded by a dynamic `import()` resolved from `document.currentScript.src` (captured while the classic script evaluates — a bare `'_content/…'` specifier is not a module specifier and throws without an import map). `pointer-drag.js` drives the resize (`.tm-scheduler-event-resize-handle`: the time grid renders a top and a bottom handle, the timeline a left and a right handle, and the keyboard resize changes the end only; a compact selected event hides the start grip; `longPressMs: 400`) and the touch move (`.tm-scheduler-timegrid-event` / `.tm-scheduler-timeline-event` / `.tm-scheduler-month-event` with `[draggable="true"]`, `pointerTypes: ['touch']`, 400 ms long-press; mouse and pen stay on HTML5 drag-and-drop). `pointer-gesture.js` supplies `createAutoScroller` for the edge scroll during a drag. A host page needs only the `scheduler.js` script tag; both helpers must be served from the same `_content/Tempo.Blazor/js/` folder, and a per-file CSP hash must list them. Contract: [docs/scheduler.md](docs/scheduler.md) and [docs/touch-interaction.md](docs/touch-interaction.md)
- `pointer-drag.js` — the shared ES-module pointer-drag helper (capture by pointerId, threshold, long-press, cancel paths); never write a second drag implementation, see [docs/touch-interaction.md](docs/touch-interaction.md)
- `pointer-gesture.js` — the shared ES-module multi-pointer helper (`attachPinch` two-finger pinch/pan with ctrl+wheel zoom, `createAutoScroller` edge auto-scroll); never write a second pinch, a host cancels its drag from the pinch's `onStart`, see [docs/touch-interaction.md](docs/touch-interaction.md)
- `signature-capture.js` — required by `TmSignatureCapture`

Split package assets live under their own static-web-asset base paths:
- `Tempo.Blazor.PdfViewer`: `_content/Tempo.Blazor.PdfViewer/js/pdf-viewer.js`
- `Tempo.Blazor.DiagramEditor`: `_content/Tempo.Blazor.DiagramEditor/js/diagram-editor.js` (classic script; it dynamically imports `diagram-editor-touch.js`; `dagre.min.js`, the d3 bundles and `diagram-arrow-select.js` are separate `<script>` tags), plus `_content/Tempo.Blazor.DiagramEditor/css/tempo-blazor-diagram-editor.css`
- `Tempo.Blazor.Wireframe`: `_content/Tempo.Blazor.Wireframe/js/wireframe-designer.js` (classic script; it dynamic-imports its own `wireframe-gestures.mjs` and, from the core package, the shared `pointer-drag.js` / `pointer-gesture.js` - so **`_content/Tempo.Blazor/js/` must be served** too: without it the canvas has no pointer gestures and logs "the pointer helpers ... could not be loaded"; its middle-button pan is the one documented hand-written gesture outside `pointer-drag.js`, see [docs/touch-interaction.md](docs/touch-interaction.md)), plus `_content/Tempo.Blazor.Wireframe/css/tempo-blazor-wireframe.css`
- `Tempo.Blazor.Spreadsheet`: `_content/Tempo.Blazor.Spreadsheet/js/spreadsheet.js` and `_content/Tempo.Blazor.Spreadsheet/js/spreadsheet-canvas.js`
- `Tempo.Blazor.NotionEditor`: `_content/Tempo.Blazor.NotionEditor/js/notion-editor.js`
- `Tempo.Blazor.Signing`: `_content/Tempo.Blazor.Signing/js/pdf-template-designer.js` (loaded by `TmPdfTemplateDesigner` as an ES module)
- `Tempo.Blazor.EmailTemplates`: `_content/Tempo.Blazor.EmailTemplates/tm-email-save-shortcut.js` (loaded by `TmEmailTemplateEditor` as an ES module; cancels the browser's Save-page default for Ctrl/⌘+S only, one listener per editor element), `tm-email-variable-insert.js` (caret insertion for the variable picker) and `tm-email-panel-focus.js` (focus recovery onto the expanded panel's toggle after a host-owned rail button, e.g. the inspector rail, unmounts). Its `__tests__/*.test.mjs` files live inside `wwwroot` (the packages' node lanes import the modules directly) and are excluded from the published static web assets by the csproj.

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

`.github/workflows/js-node-tests.yml` runs the seven Node lanes on every push/PR (overlay,
diagram-editor, pdf-viewer, wireframe, reporting-modules, email-templates, document-editor) plus the
`check:document-editor-bundle` freshness check.

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

## Component JSON Documentation

Machine-readable component docs ship as two layers:

- **Overlays** under `JsonDocumentation/` (`Components/*.json` and `Packages/<PackageId>/items/**/*.json`) are the editable source — human descriptions, examples, cssClasses. Parameter entries there are a curated subset; the merge fills the rest from source.
- **Bundles** at the repo root (`tempo-blazor*.json`, plus the `tempo-blazor-all.json` aggregate) are the generated output that consumers and the MCP server actually read. They are committed — regenerate them after touching `[Parameter]` surface:

```bash
dotnet run --project JsonDocumentation/JsonDocumentationGenerator/JsonDocumentationGenerator.csproj -- \
    JsonDocumentation generate
```

`ComponentDocumentationFreshnessTests` (`tests/Tempo.Blazor.Tests/Documentation/`) keeps both layers honest: every non-abstract `Tm*` component carrying `[Parameter]` must have an overlay entry (the undocumented set is frozen and may only shrink), overlays may not document parameters that no longer exist, and the committed bundles must cover the full settable parameter surface — so a parameter change without a regeneration fails the test.

Conventions the pipeline already encodes: `[Parameter(CaptureUnmatchedValues)]` splats (`AdditionalAttributes`) are never documented; parameters inherited from a documented base (`TmComponentBase.DataTestId`, `TestIdPrefix`) are attributed to the base's entry, not repeated per component.

## Language Note

- **Code, XML documentation, and comments**: English
- **Planning documents** (in `planning/`): Czech
- **Library localization**: English (`en`), Czech (`cs`), Slovak (`sk`) and French (`fr`) built-in, extensible. `TmResourceCompletenessTests` guards key parity: every English key must exist in the Czech, Slovak and French tables.

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
| `Tempo.Blazor.EmailTemplates` | Email template editor UI, preview, import/export dialogs, CSS and three ES modules. **No** Scriban/Mjml.Net/AngleSharp, so it runs in WebAssembly |
| `Tempo.Blazor.EmailTemplates.Abstractions` | Email model, DTOs, render/save/localization contracts, security policy. No Blazor, no engine |
| `Tempo.Blazor.EmailTemplates.Engine` | Local render engine: Scriban sandbox, Mjml.Net, AngleSharp sanitizer, local preview renderer. Reference it only in the process that renders |

---

*This file is intended for AI coding agents. For human-readable documentation, see `README.md`.*
