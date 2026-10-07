# Tempo.Blazor

Comprehensive Blazor component library with 100+ components for building modern web applications. Supports **WebAssembly, Server, and InteractiveAuto** render modes and multi-targets **.NET 8, 9, and 10**.

Key features:
- Full-featured form controls, data tables, pickers, charts, scheduler, kanban, and workflow designer
- FluentValidation integration via `Tempo.Blazor.FluentValidation`
- Built-in localization (English / French / Czech) with override support
- CSS design system based on custom properties with dark mode
- Injectable `ToastService` and `ThemeService` registered automatically
- Lightweight abstractions package (`Tempo.Blazor.Abstractions`) for use in API/service projects without UI dependencies
- Optional Tempo Reporting packages for embedded viewers, report definitions, rendering, export, and Report Server integration

## Installation

```bash
dotnet add package Tempo.Blazor
```

Optional packages:

```bash
dotnet add package Tempo.Blazor.All              # Compatibility package: core + split feature packages
dotnet add package Tempo.Blazor.Abstractions      # Interfaces only (for API/service projects)
dotnet add package Tempo.Blazor.PdfViewer         # PDF.js powered PDF viewer
dotnet add package Tempo.Blazor.Codes             # QR code and barcode components
dotnet add package Tempo.Blazor.DiagramEditor     # Diagram editor and diagram assets
dotnet add package Tempo.Blazor.Wireframe         # Wireframe editor and wireframe assets
dotnet add package Tempo.Blazor.Modeling          # Modeling editor built on DiagramEditor
dotnet add package Tempo.Blazor.Spreadsheet       # Spreadsheet editor and XLSX support
dotnet add package Tempo.Blazor.GanttXlsx         # Optional Gantt XLSX import/export helpers
dotnet add package Tempo.Blazor.DocumentEditor    # Document editor and canvas runtime
dotnet add package Tempo.Blazor.NotionEditor      # Notion-style editor and block/database UI
dotnet add package Tempo.Blazor.Signing           # Signing workflows and PDF template designer
dotnet add package Tempo.Blazor.FluentValidation   # FluentValidation integration for EditForm
dotnet add package Tempo.Reporting.Abstractions    # Report definition JSON, validation, data contracts
dotnet add package Tempo.Reporting.Engine          # Processing, layout, PDF/PNG, CSV/XLSX
dotnet add package Tempo.Blazor.Reporting          # Blazor report viewer/designer/explorer components
```

## Document Editor Cutover

`TmDocumentEditor` defaults to the canvas engine after the phase 25 cutover. Rollback and migration notes are in [docs/document-editor-canvas-cutover.md](docs/document-editor-canvas-cutover.md).

## Quick Start

### 1. Register Services

```csharp
// Program.cs
builder.Services.AddTempoBlazor();
```

To keep the pre-split all-in setup, install `Tempo.Blazor.All` and register:

```csharp
builder.Services.AddTempoBlazorAll();
```

`Tempo.Blazor.All` includes `Tempo.Blazor.Signing` and calls `AddTempoBlazorSigning()` automatically.

### Viewport scope

A modal, a dialog and a bottom sheet resolve their layout from the viewport, not from the container
their trigger sits in. Place one viewport scope as the outermost element of your layout, wrapping the
sidebar too:

```razor
<TmLayoutObserver IsViewportScope="true" IsContainer="false">
    @Body
</TmLayoutObserver>
```

`IsContainer="false"` is required: a containment context on this element would become the containing
block for every `position: fixed` overlay and pin a sheet to the app shell instead of the viewport.
With no viewport scope an overlay renders its `InitialMode`, which is desktop, so a phone would show
the desktop dialog until you add the scope. Set `InitialMode="TmLayoutMode.Mobile"` on the scope when
the first frame must already be the phone layout. See
[docs/responsive-conventions.md](docs/responsive-conventions.md).

For a lean app, reference and register only the feature packages you use:

```csharp
builder.Services.AddTempoBlazor();
builder.Services.AddTempoBlazorPdfViewer();
builder.Services.AddTempoBlazorDiagramEditor();
builder.Services.AddTempoBlazorWireframe();
builder.Services.AddTempoBlazorSpreadsheet();
builder.Services.AddTempoBlazorDocumentEditor();
builder.Services.AddTempoBlazorNotionEditor();
builder.Services.AddTempoBlazorSigning();
```

### Custom Diagram Stencils

Install `Tempo.Blazor.DiagramEditor` and register custom diagram stencils after `AddTempoBlazorDiagramEditor()`. Keep provider output as your own data and mark built-in application stencils with `DiagramStencilOrigin.TempoOriginal`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;

builder.Services.AddTempoBlazorDiagramEditor();
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IDiagramStencilProvider, MyStencilProvider>());

public sealed class MyStencilProvider : IDiagramStencilProvider
{
    public int Priority => 100;

    public IEnumerable<DiagramStencilSet> GetStencilSets()
    {
        yield return new DiagramStencilSet
        {
            Id = "my-stencils",
            NameResourceKey = "DiagramStencilSet_MyStencils",
            Stencils =
            [
                new DiagramStencil
                {
                    Id = "my-stencils.service",
                    NameResourceKey = "DiagramStencil_MyService",
                    Category = "Application",
                    Kind = DiagramStencilKind.Node,
                    Origin = DiagramStencilOrigin.TempoOriginal
                }
            ]
        };
    }
}
```

### 2. Add CSS

```html
<!-- index.html or App.razor -->
<link href="_content/Tempo.Blazor/css/tempo-blazor.bundled.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.DiagramEditor/css/tempo-blazor-diagram-editor.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.Wireframe/css/tempo-blazor-wireframe.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.Spreadsheet/css/tempo-blazor-spreadsheet.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.DocumentEditor/css/tempo-blazor-document-editor.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.NotionEditor/css/tempo-blazor-notion-editor.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.Signing/css/tempo-blazor-signing.css" rel="stylesheet" />
```

`TmPdfTemplateDesigner` loads its ES module from `_content/Tempo.Blazor.Signing/js/pdf-template-designer.js`. If your host app uses CSP or a static-asset allowlist, include that path. `TmSignatureCapture` still uses the core `_content/Tempo.Blazor/js/signature-capture.js` asset.

### 3. Add Toast Container to Layout

```razor
<!-- MainLayout.razor -->
@inherits LayoutComponentBase
@implements IDisposable
@inject ThemeService ThemeService

<div data-theme="@ThemeService.ThemeName">
    @Body
    <TmToastContainer />
</div>

@code {
    protected override void OnInitialized() => ThemeService.OnChanged += StateHasChanged;
    public void Dispose() => ThemeService.OnChanged -= StateHasChanged;
}
```

### 4. Add Imports

```razor
<!-- _Imports.razor -->
@using Tempo.Blazor.Components.Avatars
@using Tempo.Blazor.Components.Buttons
@using Tempo.Blazor.Components.DataDisplay
@using Tempo.Blazor.Components.DataTable
@using Tempo.Blazor.Components.Dropdowns
@using Tempo.Blazor.Components.Feedback
@using Tempo.Blazor.Components.Files
@using Tempo.Blazor.Components.Forms
@using Tempo.Blazor.Components.Icons
@using Tempo.Blazor.Components.Inputs
@using Tempo.Blazor.Components.Layout
@using Tempo.Blazor.Components.Navigation
@using Tempo.Blazor.Components.Notifications
@using Tempo.Blazor.Components.Pickers
@using Tempo.Blazor.Components.Scheduler
@using Tempo.Blazor.Components.Timeline
@using Tempo.Blazor.Components.Toolbar
@using Tempo.Blazor.Components.TreeView
@using Tempo.Blazor.Components.Workflow
@using Tempo.Blazor.Interfaces
@using Tempo.Blazor.Models
@using Tempo.Blazor.Services
```

### 5. Use Components

```razor
<TmButton Variant="ButtonVariant.Primary" OnClick="HandleClick">
    Click me
</TmButton>

<TmFormField Label="Name">
    <TmTextInput @bind-Value="name" Placeholder="Enter your name" />
</TmFormField>

<TmDatePicker Label="Date of birth" @bind-Value="dateOfBirth" />

<TmDataTable TItem="Person" Items="people">
    <TmDataTableColumn TItem="Person" Title="Name" Field="p => p.Name" Sortable />
    <TmDataTableColumn TItem="Person" Title="Email" Field="p => p.Email" />
</TmDataTable>
```

## Embedding do vaší aplikace

`TmReportViewer` lze vložit do běžné Blazor aplikace dvěma způsoby. Embedded režim spouští report engine v hostitelské aplikaci nad vlastním `IReportDataProvider`; Remote režim volá Tempo Report Server API přes `RemoteReportSource`.

### Embedded: lokální engine a vlastní data

```csharp
// Program.cs
using Tempo.Blazor.Reporting.Configuration;

builder.Services.AddTempoBlazorReporting();
builder.Services.AddScoped<MyReportDataProvider>();
```

```razor
@using Tempo.Blazor.Reporting.Components
@using Tempo.Blazor.Reporting.Services
@using Tempo.Reporting.Abstractions.Definitions
@inject MyReportDataProvider DataProvider

<TmReportViewer ReportSource="_source"
                TenantId="northwind"
                UserId="embedded-user"
                CultureName="en-US" />

@code {
    private IReportSource? _source;

    protected override void OnInitialized()
    {
        ReportDefinition definition = BuildDefinition();
        _source = new EmbeddedReportSource(definition, DataProvider);
    }
}
```

### Remote: Report Server API

```csharp
// Program.cs
using Tempo.Blazor.Reporting.Configuration;

builder.Services.AddTempoBlazorReporting();
builder.Services.AddHttpClient("Reports", client =>
{
    client.BaseAddress = new Uri("https://reports.example.com/");
    client.DefaultRequestHeaders.Add("X-Api-Key", builder.Configuration["Reports:ApiKey"]);
});
```

```razor
@using Tempo.Blazor.Reporting.Components
@using Tempo.Blazor.Reporting.Services
@inject IHttpClientFactory HttpClientFactory

<TmReportViewer ReportSource="_source"
                TenantId="northwind"
                UserId="embedded-user"
                CultureName="en-US" />

@code {
    private IReportSource? _source;

    protected override void OnInitialized()
    {
        _source = new RemoteReportSource(HttpClientFactory.CreateClient("Reports"), "sales-dashboard");
    }
}
```

Na serveru musí API key mapovat na tenant/application scope s oprávněními `View`, `Render` a volitelně `Export`. Demo používá hlavičku `X-Api-Key`; pro produkci ukládejte klíč mimo klientský kód, rotujte ho přes serverovou administraci a pro WebAssembly povolte CORS jen pro známé origins.

Přidejte také reporting stylesheet:

```html
<link href="_content/Tempo.Blazor.Reporting/css/tempo-blazor-reporting.css" rel="stylesheet" />
```

### Tempo Report Server (self-hosted BE server)

`RemoteReportSource` výše cílí na **Tempo Report Server** — samostatný on-premises reportovací
server postavený nad hotovým Tempo rendering enginem, jako otevřená alternativa k Telerik Report
Serveru: katalog reportů s revizemi, render do PDF/XLSX/CSV/PNG přes REST, per-folder oprávnění,
API klíče, audit a plánované doručování e-mailem/do úložiště/webhookem. Běží jako dva procesy
(REST **Api** host + InteractiveAuto **Web** portál) s autentizací přes Keycloak OIDC a perzistencí
v MSSQL.

- Přehled, architektura, quickstart a stránky/komponenty: [docs/report-server.md](docs/report-server.md)
- End-to-end tutoriál (report → nahrání přes Api → PDF → API klíč → plán e-mailem → smtp4dev):
  [docs/report-server-tutorial.md](docs/report-server-tutorial.md)
- Nasazení a konfigurace: [docs/report-server-deployment.md](docs/report-server-deployment.md)
- Rozhodnutí: [ADR-0001 MSSQL](docs/adr/0001-databaze-mssql.md), [ADR-0002 OIDC/Keycloak](docs/adr/0002-oidc-keycloak.md)
- Živé E2E důkazy: [docs/report-server-e2e-results.md](docs/report-server-e2e-results.md)

### Toast Notifications

```csharp
// Inject ToastService anywhere in your components
[Inject] ToastService Toast { get; set; } = default!;

// Show notifications
Toast.ShowSuccess("Saved successfully");
Toast.ShowError("Something went wrong");
Toast.ShowWarning("Check your input");
Toast.ShowInfo("Record updated");
```

## Document Editor Collaboration Protocol

`TmDocumentEditor` can synchronize live WYSIWYG edits through `IDocumentCollaborationProvider`. The provider boundary exchanges append-only `DocumentOperationBatch` payloads, not rendered HTML. Each batch carries a protocol version, a document id, stable operation ids, operation metadata, and one or more structured operations such as `InsertText`, `DeleteText`, `AddInlineMark`, `RemoveInlineMark`, `InsertBlock`, `UpdateBlock`, `DeleteBlock`, `CreateRevision`, `AcceptRevision`, and `RejectRevision`.

The live editor surface is owned by the JavaScript WYSIWYG runtime while the user is editing. Remote batches are applied through the JS runtime as DOM operations and then mirrored into the C# document model for persistence, export, suggestions, revision panels, and provider replay. Blazor does not re-render the live editor surface for successful remote operations.

`SetBlockAttribute("text")` is kept only as a legacy/import compatibility fallback. Rich text, formatting, images, tables, and other object changes use structured operations, most commonly `UpdateBlock` when a whole block payload is the safest representation. If a remote DOM patch fails, the component falls back to a full snapshot refresh so the browser DOM and the C# document model converge again.

See [Document Editor JS-Owned Runtime](docs/document-editor-js-owned-runtime.md) for the runtime boundary, provider flow, undo/redo transaction rules, track changes model, and E2E test guidance.

## Components

### Form Controls
| Component | Description |
|-----------|-------------|
| `TmButton` | Button with variants (Primary, Secondary, Danger, Ghost, Link), sizes, loading state |
| `TmSplitButton` | Button with dropdown menu |
| `TmCopyButton` | Copy-to-clipboard button |
| `TmTextInput` | Text input with label, error, help text, prefix/suffix |
| `TmTextArea` | Multi-line text input with auto-resize |
| `TmNumberInput` | Numeric input with +/- buttons, min/max, prefix/suffix |
| `TmCurrencyInput` | Currency input with locale-aware formatting |
| `TmSelect` | Select dropdown with options |
| `TmMultiSelect` | Multi-value select dropdown |
| `TmCascadingSelect` | Hierarchical cascading select |
| `TmCheckbox` | Checkbox with label |
| `TmToggle` | Toggle switch (`role="switch"`) |
| `TmRadioGroup` | Radio button group |
| `TmSearchInput` | Search input with debounce |
| `TmPasswordStrengthIndicator` | Password strength meter |
| `TmEntityPicker` | Async search-and-select picker for entities |
| `TmTagPicker` | Tag/keyword picker with suggestions |
| `TmExpressionEditor` | Expression editor with variable insertion |

### Date & Time Pickers
| Component | Description |
|-----------|-------------|
| `TmDatePicker` | Calendar popup date picker |
| `TmTimePicker` | Time picker (hours, minutes, optional seconds) |
| `TmTimeInput` | Inline time input field |
| `TmDateTimePicker` | Combined date + time picker |
| `TmDateRangePicker` | Dual-calendar range picker with presets |
| `TmTimeRangePicker` | Time range with duration display |
| `TmDateTimeRangePicker` | Combined date-time range picker |

### Forms
| Component | Description |
|-----------|-------------|
| `TmFormField` | Form field wrapper with label, validation, and help text |
| `TmFormRow` | Horizontal form row layout |
| `TmFormSection` | Grouped form section with heading |
| `TmInlineEdit` | Click-to-edit inline field |
| `TmValidatedField` | Field wrapper with FluentValidation integration |
| `TmValidationSummary` | Summary of all validation errors |
| `TmFormValidationMessage` | Per-field validation message |
| `TmDynamicFormRenderer` | Render forms from field definitions |

### Data Display
| Component | Description |
|-----------|-------------|
| `TmBadge` | Status badges (Soft, Filled, Outlined) |
| `TmCard` | Content card with header, body, footer |
| `TmStatCard` | KPI statistic card with trend |
| `TmEmptyState` | Placeholder for empty lists |
| `TmAvatar` / `TmAvatarGroup` | User avatars with initials fallback |
| `TmAccordion` / `TmAccordionItem` | Collapsible content sections |
| `TmChip` / `TmChipGroup` | Removable chip tags |
| `TmChangeDiff` | Side-by-side or inline diff view |
| `TmMultiViewList` | Table / Card / List view switcher |
| `TmKanbanBoard` | Kanban board with drag & drop |

### Data Table
| Component | Description |
|-----------|-------------|
| `TmDataTable` | Full-featured data table with sorting, filtering, grouping, pagination, selection, and column picker |
| `TmDataTableColumn` | Column definition with sort, filter, custom cell template, grouping, and aggregates |
| `TmPagination` | Standalone pagination |
| `TmColumnFilter` | Column filter UI |
| `TmColumnPicker` | Column visibility manager |
| `TmViewManager` | Save/load named table views |
| `TmBulkActionBar` | Floating bulk action toolbar |

### Layout
| Component | Description |
|-----------|-------------|
| `TmSidebar` | Collapsible sidebar navigation |
| `TmTopBar` | Top navigation bar |
| `TmBreadcrumbs` | Breadcrumb navigation |
| `TmSection` | Content section with optional heading and padding |
| `TmToggleSection` | Collapsible section with toggle header |
| `TmCommandPalette` | Ctrl+K command palette |
| `TmKeyboardShortcutsHelp` | Keyboard shortcuts reference overlay |
| `TmDrawer` | Slide-in side panel |

### Navigation
| Component | Description |
|-----------|-------------|
| `TmTabs` / `TmTabPanel` | Tab navigation (Line, Pill, Enclosed) |
| `TmContextMenu` / `TmContextMenuItem` | Right-click context menu |

### Toolbar
| Component | Description |
|-----------|-------------|
| `TmToolbar` | Action toolbar container |
| `TmToolbarButton` | Button for use inside `TmToolbar` |
| `TmToolbarDivider` | Visual separator inside `TmToolbar` |

### Dropdowns
| Component | Description |
|-----------|-------------|
| `TmDropdown` / `TmDropdownItem` | Generic dropdown menu |
| `TmFilterableDropdown` | Dropdown with search/filter input |

### Feedback
| Component | Description |
|-----------|-------------|
| `TmSpinner` | Loading spinner |
| `TmSkeleton` | Content skeleton placeholder |
| `TmAlert` | Alert banner (Info, Success, Warning, Error) |
| `TmDialog` | Alert / Confirm / Prompt dialog |
| `TmModal` | Modal overlay (5 sizes) |
| `TmToastContainer` | Toast notifications renderer — place once in layout |
| `TmProgressBar` | Progress bar (determinate, indeterminate, segmented) |
| `TmTooltip` | Hover tooltip |
| `TmPopover` | Click-triggered popover |
| `TmAutoSaveIndicator` | Auto-save status indicator |

### Notifications
| Component | Description |
|-----------|-------------|
| `TmNotificationBell` | Notification bell with dropdown list |

### Files & Gallery
| Component | Description |
|-----------|-------------|
| `TmFileDropZone` | Drag & drop file upload zone |
| `TmAttachmentManager` | Attachment list with upload/download/delete |
| `TmImageGallery` | Image gallery with grid/gallery views |
| `TmLightbox` | Full-screen image lightbox with keyboard nav |

### Filters
| Component | Description |
|-----------|-------------|
| `TmFilterChip` | Removable filter chip for active filter display |
| `TmFilterBuilder` | Dynamic query filter builder |

### Activity & Collaboration
| Component | Description |
|-----------|-------------|
| `TmActivityLog` | Tabbed activity view (Timeline / Comments / Attachments) |
| `TmActivityTimeline` | Reverse-chronological activity feed |
| `TmActivityComments` | Comment list with edit/delete + rich text editor |
| `TmActivityAttachments` | Attachment list with chunked upload |
| `TmRichEditorFull` | Full rich text editor (bold, italic, lists, tables, links, images, mentions) |
| `TmRichEditorSimple` | Simplified rich text editor |

### Timeline
| Component | Description |
|-----------|-------------|
| `TmTimeline` | Standalone vertical timeline component |

### Scheduler
| Component | Description |
|-----------|-------------|
| `TmScheduler` | Full calendar/scheduler with multiple views |
| `TmSchedulerDayView` | Day view for the scheduler |
| `TmSchedulerWeekView` | Week view for the scheduler |
| `TmSchedulerMonthView` | Month view for the scheduler |
| `TmSchedulerAgendaView` | Agenda/list view for the scheduler |
| `TmSchedulerTimelineView` | Horizontal timeline view for the scheduler |

### Import & Export
| Component | Description |
|-----------|-------------|
| `TmImportWizard` / `TmImportWizardStep` | Step-by-step data import wizard |
| `TmImportPreview` | Preview imported data before confirming |
| `TmExportOptions` | Export format and options dialog |

### Advanced
| Component | Description |
|-----------|-------------|
| `TmChart` | SVG charts (Bar, Line, Pie, Donut, HorizontalBar) |
| `TmDashboard` | Widget dashboard with drag/resize grid |
| `TmTreeView` | Hierarchical tree with lazy loading |
| `TmStepper` | Step-by-step wizard/progress stepper |
| `TmWorkflowDesignerCanvas` | Visual workflow editor (drag nodes, draw transitions, zoom/pan) |
| `TmIcon` | SVG icon with extensible registry |

## Localization

Built-in support for English and Czech. Override with your own `ITmLocalizer`:

```csharp
builder.Services.AddTempoBlazor();
builder.Services.AddSingleton<ITmLocalizer, MyLocalizer>();
```

Set culture:
```csharp
var culture = new CultureInfo("cs");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
```

## Theming

### Dark Mode

`ThemeService` is automatically registered by `AddTempoBlazor()`. Subscribe to `OnChanged` in your layout to re-render when the theme changes:

```razor
@inherits LayoutComponentBase
@implements IDisposable
@inject ThemeService ThemeService

<div data-theme="@ThemeService.ThemeName">
    <button @onclick="ThemeService.Toggle">Toggle theme</button>
    @Body
</div>

@code {
    protected override void OnInitialized() => ThemeService.OnChanged += StateHasChanged;
    public void Dispose() => ThemeService.OnChanged -= StateHasChanged;
}
```

### Custom Tokens

Override CSS custom properties to match your brand:

```css
:root {
    --tm-color-primary: #your-brand-color;
    --tm-color-primary-hover: #your-hover-color;
    --tm-font-sans: 'Inter', sans-serif;
    --tm-radius-md: 8px;
}
```

## FluentValidation

```bash
dotnet add package Tempo.Blazor.FluentValidation
```

```csharp
// Program.cs — auto-scan assemblies for validators
builder.Services.AddTempoFluentValidation(typeof(MyValidator).Assembly);
```

```razor
@using Tempo.Blazor.FluentValidation

<EditForm Model="model" OnValidSubmit="Submit">
    <FluentValidationValidator />

    <TmFormField Label="Name">
        <TmTextInput @bind-Value="model.Name" />
    </TmFormField>
</EditForm>
```

`FluentValidationValidator` is provided by `Tempo.Blazor.FluentValidation` and wires up all registered `IValidator<T>` instances to the `EditContext`.

## Data Providers

Reference `Tempo.Blazor.Abstractions` from your API/service project to implement server-side data:

```csharp
public class PersonDataProvider : IDataTableDataProvider<Person>
{
    public async Task<PagedResult<Person>> GetDataAsync(DataTableQuery query, CancellationToken ct = default)
    {
        // query.Page, query.PageSize — paging (1-based)
        // query.SortColumn, query.SortDescending — sorting
        // query.Filters — column filter predicates
        // query.SearchText — global search
        // query.GroupByColumns — server-side grouping
    }
}
```

Available provider interfaces (all in `Tempo.Blazor.Interfaces`):
- `IDataTableDataProvider<TItem>` — server-side table data with paging, sorting, filtering, grouping
- `IDropdownDataProvider<TItem>` — async dropdown/entity picker search
- `IImageGalleryDataProvider` — gallery images with ticket-based URLs
- `IFileAttachmentProvider` — attachment CRUD with chunked upload
- `IDataTableViewProvider` — saved table views (personal/tenant scoped)
- `IDashboardProvider` — dashboard configuration persistence
- `IWidgetRegistry` — dashboard widget definitions
- `IScheduleDataProvider` — server-side scheduler events by date range
- `IMentionDataProvider` — user mention suggestions in rich editors

## Custom Icons

```csharp
// Register an inline SVG path (content inside <svg> tags)
IconRegistry.Register("my-icon", "<path d='M12 2L2 22h20L12 2z'/>");

// Or implement IIconProvider for batch registration
IconRegistry.RegisterProvider(new MyIconProvider());
```

```razor
<TmIcon Name="my-icon" Size="IconSize.Lg" Color="IconColor.Primary" />
```

## Requirements

- .NET 8, 9, or 10
- Blazor WebAssembly, Server, or InteractiveAuto

## License

MIT
