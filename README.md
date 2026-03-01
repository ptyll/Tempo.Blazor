# Tempo.Blazor

Comprehensive Blazor component library with 100+ components for building modern web applications. Designed for .NET 10 Blazor WebAssembly with full localization, dark mode, FluentValidation, and a CSS design system based on custom properties.

## Installation

```bash
dotnet add package Tempo.Blazor
```

Optional packages:

```bash
dotnet add package Tempo.Blazor.Abstractions      # Interfaces only (for API/service projects)
dotnet add package Tempo.Blazor.FluentValidation   # FluentValidation integration for EditForm
```

## Quick Start

### 1. Register Services

```csharp
// Program.cs
builder.Services.AddTempoBlazor();
```

### 2. Add CSS

```html
<!-- index.html or App.razor -->
<link href="_content/Tempo.Blazor/css/tempo-blazor.css" rel="stylesheet" />
```

### 3. Add Imports

```razor
<!-- _Imports.razor -->
@using Tempo.Blazor.Components.Buttons
@using Tempo.Blazor.Components.Inputs
@using Tempo.Blazor.Components.DataDisplay
@using Tempo.Blazor.Components.Layout
@using Tempo.Blazor.Components.Feedback
@using Tempo.Blazor.Components.Pickers
@using Tempo.Blazor.Components.DataTable
```

### 4. Use Components

```razor
<TmButton Variant="ButtonVariant.Primary" OnClick="HandleClick">
    Click me
</TmButton>

<TmTextInput Label="Name" @bind-Value="name" Placeholder="Enter your name" />

<TmDatePicker Label="Date of birth" @bind-Value="dateOfBirth" />

<TmDataTable TItem="Person" Items="people">
    <TmDataTableColumn TItem="Person" Title="Name" Field="p => p.Name" Sortable />
    <TmDataTableColumn TItem="Person" Title="Email" Field="p => p.Email" />
</TmDataTable>
```

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
| `TmSelect` | Select dropdown with options |
| `TmCheckbox` | Checkbox with label |
| `TmToggle` | Toggle switch (`role="switch"`) |
| `TmRadioGroup` | Radio button group |
| `TmSearchInput` | Search input with debounce |
| `TmPasswordStrengthIndicator` | Password strength meter |
| `TmEntityPicker` | Async search-and-select picker for entities |
| `TmExpressionEditor` | Expression editor with variable insertion |

### Date & Time Pickers
| Component | Description |
|-----------|-------------|
| `TmDatePicker` | Calendar popup date picker |
| `TmTimePicker` | Time picker (hours, minutes, optional seconds) |
| `TmDateTimePicker` | Combined date + time picker |
| `TmDateRangePicker` | Dual-calendar range picker with presets |
| `TmTimeRangePicker` | Time range with duration display |
| `TmDateTimeRangePicker` | Combined date-time range picker |

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
| `TmMultiViewList` | Table / Card / List view switcher |

### Data Table
| Component | Description |
|-----------|-------------|
| `TmDataTable` | Full-featured data table with sorting, filtering, pagination, selection, column picker |
| `TmDataTableColumn` | Column definition with sort, filter, custom templates |
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
| `TmCommandPalette` | Ctrl+K command palette |
| `TmDrawer` | Slide-in side panel |
| `TmTabs` / `TmTabPanel` | Tab navigation (Line, Pill, Enclosed) |

### Feedback
| Component | Description |
|-----------|-------------|
| `TmSpinner` | Loading spinner |
| `TmSkeleton` | Content skeleton placeholder |
| `TmAlert` | Alert banner (Info, Success, Warning, Error) |
| `TmDialog` | Alert / Confirm / Prompt dialog |
| `TmModal` | Modal overlay (5 sizes) |
| `TmToastContainer` | Toast notifications |
| `TmProgressBar` | Progress bar (determinate, indeterminate, segmented) |
| `TmTooltip` | Hover tooltip |
| `TmPopover` | Click-triggered popover |
| `TmNotificationBell` | Notification dropdown |

### Files & Gallery
| Component | Description |
|-----------|-------------|
| `TmFileDropZone` | Drag & drop file upload zone |
| `TmAttachmentManager` | Attachment list with upload/download/delete |
| `TmImageGallery` | Image gallery with grid/gallery views |
| `TmLightbox` | Full-screen image lightbox with keyboard nav |

### Activity & Collaboration
| Component | Description |
|-----------|-------------|
| `TmActivityLog` | Tabbed activity view (Timeline / Comments / Attachments) |
| `TmActivityTimeline` | Reverse-chronological activity feed |
| `TmActivityComments` | Comment list with edit/delete + rich text editor |
| `TmActivityAttachments` | Attachment list with chunked upload |
| `TmRichEditorFull` | Full rich text editor (bold, italic, lists, tables, links, images, mentions) |
| `TmRichEditorSimple` | Simplified rich text editor |

### Advanced
| Component | Description |
|-----------|-------------|
| `TmChart` | SVG charts (Bar, Line, Pie, Donut, HorizontalBar) |
| `TmDashboard` | Widget dashboard with drag/resize grid |
| `TmKanbanBoard` | Kanban board with drag & drop |
| `TmTreeView` | Hierarchical tree with lazy loading |
| `TmWorkflowDesignerCanvas` | Visual workflow editor (drag nodes, draw transitions, zoom/pan) |
| `TmFilterBuilder` | Dynamic query filter builder |
| `TmDynamicFormRenderer` | Render forms from field definitions |
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

```csharp
builder.Services.AddScoped<ThemeService>();

// In layout:
<div data-theme="@ThemeService.ThemeName">
    <button @onclick="ThemeService.Toggle">Toggle theme</button>
    @Body
</div>
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

```csharp
dotnet add package Tempo.Blazor.FluentValidation
```

```csharp
// Program.cs — auto-scan assemblies for validators
builder.Services.AddTempoFluentValidation(typeof(MyValidator).Assembly);
```

```razor
<EditForm Model="model" OnValidSubmit="Submit">
    <FluentValidationValidator />

    <TmFormField Label="Name">
        <TmTextInput @bind-Value="model.Name" />
        <ValidationMessage For="() => model.Name" />
    </TmFormField>
</EditForm>
```

## Data Providers

Reference `Tempo.Blazor.Abstractions` from your API/service project to implement server-side data:

```csharp
public class PersonDataProvider : IDataTableDataProvider<Person>
{
    public async Task<PagedResult<Person>> GetDataAsync(DataTableQuery query, CancellationToken ct)
    {
        // Query your database using query.Page, query.PageSize,
        // query.SortColumn, query.Filters, query.SearchText
    }
}
```

Available provider interfaces:
- `IDataTableDataProvider<TItem>` — server-side table data
- `IDropdownDataProvider<TItem>` — async dropdown search
- `IImageGalleryDataProvider` — gallery images with ticket-based URLs
- `IFileAttachmentProvider` — attachment CRUD with chunked upload
- `IDataTableViewProvider` — saved table views (personal/tenant scoped)
- `IDashboardProvider` — dashboard configuration persistence
- `IWidgetRegistry` — dashboard widget definitions

## Custom Icons

```csharp
// Register inline SVG
IconRegistry.Register("my-icon", "<path d='M12 2L2 22h20L12 2z'/>");

// Or implement a provider
IconRegistry.RegisterProvider(new MyIconProvider());
```

```razor
<TmIcon Name="my-icon" Size="IconSize.Lg" Color="IconColor.Primary" />
```

## Requirements

- .NET 10.0+
- Blazor WebAssembly

## License

MIT
