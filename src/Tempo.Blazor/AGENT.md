# Tempo.Blazor — AI Agent Guide

This document is designed for AI coding assistants (Claude, Copilot, Cursor, etc.) working with the Tempo.Blazor component library. It ships inside the NuGet package so that AI agents in consuming projects understand the library's API, conventions, and patterns.

## Package Overview

**Tempo.Blazor** is a Blazor component library with 100+ components. Supports WebAssembly, Server, and InteractiveAuto render modes. Multi-targets .NET 8.0, 9.0, and 10.0. All components use the `Tm` prefix (e.g. `TmButton`, `TmTextInput`).

Three NuGet packages:
- **Tempo.Blazor** — UI components (depends on Abstractions)
- **Tempo.Blazor.Abstractions** — Interfaces and models only (zero UI dependencies, safe for API projects)
- **Tempo.Blazor.FluentValidation** — Optional FluentValidation integration for EditForm

## Setup in Consuming Application

```csharp
// Program.cs (WASM, Server, or both for InteractiveAuto)
builder.Services.AddTempoBlazor();      // registers ITmLocalizer, ThemeService, ToastService

// Optional: FluentValidation
builder.Services.AddTempoFluentValidation(typeof(MyValidator).Assembly);
```

```html
<!-- index.html -->
<link href="_content/Tempo.Blazor/css/tempo-blazor.css" rel="stylesheet" />

<!-- Required only if using TmDashboard or TmWorkflowDesignerCanvas: -->
<script src="_content/Tempo.Blazor/js/dashboard.js"></script>
<script src="_content/Tempo.Blazor/js/workflow-designer.js"></script>

<!-- Required only if using TmRichEditorFull / TmRichEditorSimple: -->
<script src="_content/Tempo.Blazor/js/richEditor.js"></script>
```

```razor
<!-- _Imports.razor -->
@using Tempo.Blazor.Components.Buttons
@using Tempo.Blazor.Components.Inputs
@using Tempo.Blazor.Components.DataDisplay
@using Tempo.Blazor.Components.DataTable
@using Tempo.Blazor.Components.Dropdowns
@using Tempo.Blazor.Components.Feedback
@using Tempo.Blazor.Components.Files
@using Tempo.Blazor.Components.Filters
@using Tempo.Blazor.Components.Forms
@using Tempo.Blazor.Components.Gallery
@using Tempo.Blazor.Components.Icons
@using Tempo.Blazor.Components.Layout
@using Tempo.Blazor.Components.Navigation
@using Tempo.Blazor.Components.Pickers
@using Tempo.Blazor.Components.Tags
@using Tempo.Blazor.Components.Timeline
@using Tempo.Blazor.Components.Toolbar
@using Tempo.Blazor.Components.TreeView
@using Tempo.Blazor.Components.Workflow
@using Tempo.Blazor.Components.Activity
@using Tempo.Blazor.Components.Charts
@using Tempo.Blazor.Components.Dashboard
@using Tempo.Blazor.Components.Notifications
```

---

## Component Reference

### Buttons

**TmButton** — Primary UI button
```razor
<TmButton Variant="ButtonVariant.Primary"
          Size="ButtonSize.Sm"
          Type="ButtonType.Submit"
          Disabled="false"
          IsLoading="false"
          OnClick="HandleClick">
    Click me
</TmButton>
```
- `Variant`: Primary | Secondary | Danger | Ghost | Link
- `Size`: null (default) | Xs | Sm | Lg
- `Type`: Button | Submit | Reset
- `IsLoading`: shows spinner, disables click
- `Icon`, `IconPosition`: optional icon before/after text

**TmSplitButton** — Button with dropdown actions
```razor
<TmSplitButton Text="Save" OnClick="Save" Variant="ButtonVariant.Primary">
    <TmDropdownItem Value="draft" OnClick="SaveAsDraft">Save as Draft</TmDropdownItem>
    <TmDropdownItem Value="publish" OnClick="Publish">Publish</TmDropdownItem>
</TmSplitButton>
```

**TmCopyButton** — Copy text to clipboard
```razor
<TmCopyButton Text="@textToCopy" />
```

---

### Form Inputs

All inputs support: `Label`, `Placeholder`, `Error`, `HelpText`, `Disabled`, `@bind-Value`.

**TmTextInput** — Single-line text input
```razor
<TmTextInput Label="Email" @bind-Value="email" Placeholder="you@example.com"
             Error="@errorMsg" Prefix="@" Required="true" />
```
- `Prefix`, `Suffix`: decorative text inside field
- `Type`: "text" | "email" | "password" | "url" | etc.

**TmTextArea** — Multi-line text
```razor
<TmTextArea Label="Description" @bind-Value="desc" Rows="4" MaxLength="500" />
```

**TmNumberInput** — Numeric with +/- buttons
```razor
<TmNumberInput Label="Quantity" @bind-Value="qty" Min="0" Max="100" Step="1"
               ShowButtons="true" Prefix="$" Suffix="USD" />
```
- `Value` type: `int?`

**TmSelect** — Dropdown select
```razor
<TmSelect TValue="string" Label="Country" @bind-Value="country"
          Options="countryOptions" />
```
- `Options`: `IReadOnlyList<ISelectOption<TValue>>`
- Use `SelectOption<T>.From(value, label)` to create options

**TmCheckbox** — Checkbox
```razor
<TmCheckbox Label="Accept terms" @bind-Value="accepted" />
```

**TmToggle** — Toggle switch
```razor
<TmToggle Label="Dark mode" @bind-Value="isDark" />
```
- Renders `role="switch"` with `aria-checked`

**TmRadioGroup** — Radio buttons
```razor
<TmRadioGroup TValue="string" Label="Size" @bind-Value="size"
              Options="sizeOptions" Layout="RadioLayout.Horizontal" />
```

**TmSearchInput** — Search with debounce
```razor
<TmSearchInput @bind-Value="searchTerm" Placeholder="Search..."
               DebounceMs="300" OnSearch="HandleSearch" />
```

**TmEntityPicker** — Async entity search & select
```razor
<TmEntityPicker TItem="User" TValue="int"
                @bind-Value="selectedUserId"
                SearchProvider="SearchUsers"
                ValueSelector="u => u.Id"
                DisplaySelector="u => u.FullName"
                Label="Assign to" MinSearchLength="2" Debounce="300" />
```

**TmExpressionEditor** — Expression with variable insertion
```razor
<TmExpressionEditor @bind-Value="expression"
                    Variables="variables" Label="Formula" />
```
- `Variables`: `IReadOnlyList<ExpressionVariable>` (Name, Description, Type)
- Inserts `{{VariableName}}` on variable click

---

### Date & Time Pickers

**TmDatePicker** — Calendar popup
```razor
<TmDatePicker Label="Date" @bind-Value="date" Min="DateOnly.MinValue" Max="DateOnly.MaxValue"
              Format="yyyy-MM-dd" />
```
- `Value` type: `DateOnly?`

**TmTimePicker** — Time picker
```razor
<TmTimePicker Label="Time" @bind-Value="time" ShowSeconds="false" />
```
- `Value` type: `TimeOnly?`

**TmDateTimePicker** — Combined
```razor
<TmDateTimePicker Label="When" @bind-Value="dateTime" />
```
- `Value` type: `DateTime?`

**TmDateRangePicker** — Dual-calendar range
```razor
<TmDateRangePicker Label="Period" @bind-Value="range" Presets="presets" />
```
- `Value` type: `(DateOnly? Start, DateOnly? End)`
- `Presets`: `IReadOnlyList<DateRangePreset>` — use `DateRangePresets.Today`, `.LastWeek`, etc.

**TmTimeRangePicker** — Time range with duration
```razor
<TmTimeRangePicker Label="Shift" @bind-Value="timeRange" />
```
- `Value` type: `(TimeOnly? Start, TimeOnly? End)`

**TmDateTimeRangePicker** — Combined range
```razor
<TmDateTimeRangePicker Label="Period" @bind-Value="dtRange" />
```
- `Value` type: `(DateTime? Start, DateTime? End)`

---

### Data Table

```razor
<TmDataTable TItem="Person" Items="people"
             Sortable="true" Filterable="true" Selectable="true"
             PageSize="25" ShowPagination="true"
             OnRowClick="HandleRowClick">
    <TmDataTableColumn TItem="Person" Title="Name" Field="p => p.Name" Sortable Filterable />
    <TmDataTableColumn TItem="Person" Title="Email" Field="p => p.Email" />
    <TmDataTableColumn TItem="Person" Title="Age" Field="p => p.Age" Sortable>
        <CellTemplate>@context.Age years</CellTemplate>
    </TmDataTableColumn>
</TmDataTable>
```

Server-side data:
```razor
<TmDataTable TItem="Person" DataProvider="dataProvider" />
```
- `DataProvider`: `IDataTableDataProvider<TItem>` — implement `GetDataAsync(DataTableQuery, CancellationToken)`

Related sub-components: `TmPagination`, `TmColumnFilter`, `TmColumnPicker`, `TmViewManager`, `TmBulkActionBar`.

---

### Dropdowns

**TmDropdown** — Simple dropdown menu
```razor
<TmDropdown Text="Actions">
    <TmDropdownItem Value="edit" Icon="edit" OnClick="Edit">Edit</TmDropdownItem>
    <TmDropdownItem Value="delete" Icon="trash" OnClick="Delete">Delete</TmDropdownItem>
</TmDropdown>
```

**TmFilterableDropdown** — Searchable dropdown with async data
```razor
<TmFilterableDropdown TValue="string" @bind-Value="country"
                      DataProvider="countryProvider"
                      Label="Country" Placeholder="Search countries..." />
```
- `DataProvider`: `IDropdownDataProvider<DropdownItem<TValue>>`

---

### Data Display

**TmBadge**
```razor
<TmBadge Text="Active" Variant="BadgeVariant.Soft" Style="BadgeStyle.Success" Size="BadgeSize.Sm" />
```

**TmCard**
```razor
<TmCard Header="Card Title" Variant="CardVariant.Elevated">
    <p>Card body content</p>
    <FooterContent>
        <TmButton>Action</TmButton>
    </FooterContent>
</TmCard>
```
- NOTE: Use `Header` (not `Title`) for the card title

**TmStatCard** — KPI card
```razor
<TmStatCard Title="Revenue" Value="$12,450" Change="+12.5%" IsPositive="true" Icon="dollar" />
```

**TmAccordion**
```razor
<TmAccordion Multiple="false">
    <TmAccordionItem Title="Section 1">Content 1</TmAccordionItem>
    <TmAccordionItem Title="Section 2" Subtitle="Optional subtitle">Content 2</TmAccordionItem>
</TmAccordion>
```

**TmChip**
```razor
<TmChipGroup>
    <TmChip Label="Tag 1" Variant="ChipVariant.Soft" Removable OnRemove="() => Remove(tag)" />
    <TmChip Label="Tag 2" Clickable Selected="true" OnClick="Toggle" />
</TmChipGroup>
```

**TmKanbanBoard** — Drag & drop kanban
```razor
<TmKanbanBoard TItem="TaskItem" Columns="columns" Items="tasks"
               ColumnSelector="t => t.Status"
               OnItemMoved="HandleMove">
    <CardTemplate>
        <h4>@context.Title</h4>
        <p>@context.Description</p>
    </CardTemplate>
</TmKanbanBoard>
```
- `Columns`: `IReadOnlyList<KanbanColumn>` (Id, Title, Color, MaxItems)

---

### Layout

**TmSidebar** — Collapsible navigation
```razor
<TmSidebar Items="navItems" IsCollapsed="false"
           OnItemClick="Navigate" />
```
- `Items`: `IReadOnlyList<ISidebarNavItem>`

**TmTopBar** — Top navigation bar
```razor
<TmTopBar Title="My App" User="currentUser"
          OnMenuToggle="ToggleSidebar" />
```

**TmBreadcrumbs**
```razor
<TmBreadcrumbs Items="crumbs" />
```
- `Items`: `IReadOnlyList<IBreadcrumbItem>`

**TmCommandPalette** — Ctrl+K
```razor
<TmCommandPalette Actions="actions" />
```
- `Actions`: `IReadOnlyList<ICommandPaletteAction>`

**TmDrawer** — Slide-in panel
```razor
<TmDrawer @bind-IsOpen="drawerOpen" Position="DrawerPosition.Right"
          Width="400px" Title="Details" ShowOverlay="true">
    <p>Drawer content</p>
    <FooterContent>
        <TmButton OnClick="() => drawerOpen = false">Close</TmButton>
    </FooterContent>
</TmDrawer>
```

**TmTabs** — Tab navigation
```razor
<TmTabs @bind-ActiveTabId="activeTab" Variant="TabVariant.Line">
    <TmTabPanel Id="tab1" Title="General" Icon="settings">
        Tab 1 content
    </TmTabPanel>
    <TmTabPanel Id="tab2" Title="Advanced" Badge="3">
        Tab 2 content
    </TmTabPanel>
</TmTabs>
```
- `Variant`: Line | Pill | Enclosed

---

### Feedback

**TmAlert**
```razor
<TmAlert Severity="AlertSeverity.Warning" Variant="AlertVariant.Soft"
         Title="Warning" Dismissable OnDismiss="Hide">
    Something needs attention.
</TmAlert>
```

**TmDialog** — Alert / Confirm / Prompt
```razor
<TmDialog Show="showDialog" Type="DialogType.Confirm" Variant="DialogVariant.Warning"
          Title="Delete item?" Message="This cannot be undone."
          IsDangerous="true" OnResult="HandleResult" />
```
- `OnResult`: `EventCallback<bool?>` — true = OK, false = Cancel
- `OnPromptResult`: `EventCallback<string?>` — for `DialogType.Prompt`

**TmModal** — Overlay modal
```razor
<TmModal Show="showModal" Title="Edit User" Size="ModalSize.Large"
         CloseOnEscape="true" CloseOnOverlayClick="true"
         OnClose="() => showModal = false" OnOk="Save">
    <p>Modal content</p>
    <Footer>
        <TmButton Variant="ButtonVariant.Ghost" OnClick="Cancel">Cancel</TmButton>
        <TmButton OnClick="Save">Save</TmButton>
    </Footer>
</TmModal>
```
- `Size`: Small | Medium | Large | XLarge | Fullscreen

**TmToastContainer** + ToastService
```razor
<!-- In layout, once: -->
<TmToastContainer Position="ToastPosition.TopRight" MaxVisible="5" />
```
```csharp
// From any component:
@inject ToastService Toast
Toast.Show("Saved!", ToastSeverity.Success);
```

**TmProgressBar**
```razor
<TmProgressBar Value="65" Max="100" ShowLabel Variant="ProgressBarVariant.Success"
               Striped Animated />
<TmProgressBar Indeterminate />  <!-- continuous animation -->
```

**TmTooltip**
```razor
<TmTooltip Text="More info" Position="TooltipPosition.Top">
    <span>Hover me</span>
</TmTooltip>
```

**TmPopover**
```razor
<TmPopover Position="PopoverPosition.Bottom" ShowArrow>
    <TriggerContent><TmButton>Open</TmButton></TriggerContent>
    <ChildContent>Popover content here</ChildContent>
</TmPopover>
```

---

### Files & Gallery

**TmFileDropZone** — Drag & drop upload
```razor
<TmFileDropZone Multiple="true" Accept="image/*,.pdf"
                OnFilesSelected="HandleFiles" MaxFileCount="10" />
```
- `OnFilesSelected`: `EventCallback<InputFileChangeEventArgs>` (Blazor `InputFile`)
- Access files: `e.GetMultipleFiles()` → `IBrowserFile`

**TmAttachmentManager** — Full attachment CRUD
```razor
<TmAttachmentManager Provider="attachmentProvider" EntityId="@entityId"
                     OnDeleted="HandleDeleted" />
```
- `Provider`: `IFileAttachmentProvider`

**TmImageGallery** — Gallery with lightbox
```razor
<TmImageGallery DataProvider="galleryProvider" EntityId="@entityId"
                CanDelete="true" OnImageDeleted="Refresh" />
```
- `DataProvider`: `IImageGalleryDataProvider`

---

### Activity & Collaboration

**TmActivityLog** — Tabbed activity view
```razor
<TmActivityLog EntityId="@entityId"
               TimelineEntries="entries"
               CommentProvider="commentService"
               AttachmentProvider="attachmentProvider" />
```
- Tabs: Timeline | Comments | Attachments

**TmRichEditorFull** — Full rich text editor
```razor
<TmRichEditorFull @bind-Value="htmlContent"
                  MentionProvider="mentionService"
                  Placeholder="Type something..." />
```
- Toolbar: bold, italic, underline, strikethrough, lists, headings, links, images, tables, video, find/replace
- `MentionProvider`: `IMentionDataProvider` — for @mention autocomplete

**TmRichEditorSimple** — Simplified editor (bold, italic, lists only)

---

### Charts

**TmChart** — SVG charts (no JS dependency)
```razor
<TmChart Type="ChartType.Bar" Data="chartData" Height="300px"
         ShowLegend ShowValues OnSegmentClick="HandleClick" />
```
- `Type`: Bar | Line | Pie | Donut | HorizontalBar
- `Data`: `ChartData { Labels, Datasets }` — see `ChartModels.cs`

---

### Dashboard

**TmDashboard** — Widget grid with drag & resize
```razor
<TmDashboard DashboardId="@dashId" UserId="@userId" AllowEdit="true"
             OnDashboardChanged="HandleChange" />
```
- Requires `IDashboardProvider` and `IWidgetRegistry` in DI
- Requires `<script src="_content/Tempo.Blazor/js/dashboard.js"></script>`

---

### Workflow Designer

**TmWorkflowDesignerCanvas** — Visual workflow editor
```razor
<TmWorkflowDesignerCanvas @bind-Definition="definition"
                          ShowGrid="true" GridSize="20"
                          ReadOnly="false"
                          OnStateSelected="HandleStateSelected"
                          OnTransitionSelected="HandleTransitionSelected"
                          OnZoomLevelChanged="HandleZoom" />
```
- Requires `<script src="_content/Tempo.Blazor/js/workflow-designer.js"></script>`
- `Definition`: `WorkflowCanvasDefinition` with `States` and `Transitions`
- Interactive: drag nodes, draw transitions from ports, zoom/pan, context menu
- Public methods: `AddState(type, x, y)`, `RemoveState(id)`, `RemoveTransition(id)`, `SetZoom(scale)`, `FitToView()`

Companion components:
- `TmWorkflowToolbox` — sidebar with add/delete/zoom buttons
- `TmWorkflowPropertiesPanel` — edit selected state/transition properties
- `TmWorkflowMinimap` — overview thumbnail with viewport indicator

---

### Tree View

**TmTreeView** — Hierarchical tree
```razor
<TmTreeView TKey="string" Nodes="treeNodes"
            OnNodeSelected="HandleSelect"
            OnNodeExpanded="LoadChildren" />
```
- `Nodes`: `IReadOnlyList<ITreeNode<TKey>>`
- Supports lazy loading via `OnNodeExpanded`

---

### Tags

**TmTagPicker** — Tag selection with create
```razor
<TmTagPicker @bind-Value="selectedTags" AvailableTags="allTags"
             AllowCreate="true" OnTagCreated="HandleNew" />
```
- `Value` / `AvailableTags`: `IReadOnlyList<ITag>`

---

### Filters

**TmFilterBuilder** — Dynamic filter construction
```razor
<TmFilterBuilder Fields="filterFields" @bind-ActiveFilters="filters" />
```
- `Fields`: `IReadOnlyList<FilterDefinition>`
- `ActiveFilters`: `IReadOnlyList<ActiveFilter>`

**TmFilterChip** — Single removable filter display
```razor
<TmFilterChip Label="Status: Active" OnRemove="ClearFilter" />
```

---

### Forms

**TmFormSection** / **TmFormRow** / **TmFormField** — Form layout
```razor
<TmFormSection Title="Personal Info" Description="Basic details">
    <TmFormRow>
        <TmFormField Label="First Name" Required>
            <TmTextInput @bind-Value="firstName" />
        </TmFormField>
        <TmFormField Label="Last Name">
            <TmTextInput @bind-Value="lastName" />
        </TmFormField>
    </TmFormRow>
</TmFormSection>
```

**TmDynamicFormRenderer** — Render forms from metadata
```razor
<TmDynamicFormRenderer Fields="fieldDefs" @bind-Values="formValues"
                       Columns="2" ReadOnly="false" />
```
- `Fields`: `IReadOnlyList<DynamicFieldDefinition>` (Name, FieldType, Label, Options, etc.)
- `Values`: `Dictionary<string, object?>`

**TmValidatedField** — Input with EditContext validation
```razor
<EditForm Model="model">
    <TmValidatedField Label="Email" @bind-Value="model.Email"
                      ValueExpression="() => model.Email" Required />
</EditForm>
```

---

### Context Menu

**TmContextMenu** — Click-triggered menu
```razor
<TmContextMenu>
    <Trigger><TmButton Variant="ButtonVariant.Ghost">...</TmButton></Trigger>
    <ChildContent>
        <TmContextMenuItem Label="Edit" Icon="edit" OnClick="Edit" />
        <TmContextMenuItem IsDivider />
        <TmContextMenuItem Label="Delete" Icon="trash" IsDangerous OnClick="Delete" />
    </ChildContent>
</TmContextMenu>
```

---

### Icons

```razor
<TmIcon Name="check" Size="IconSize.Md" Color="IconColor.Success" />
```
- Built-in icons: see `IconNames` static class for available names
- `Size`: Xs | Sm | Md | Lg | Xl
- `Color`: Current | Primary | Danger | Success | Warning | Muted

Register custom icons:
```csharp
IconRegistry.Register("custom-icon", "<path d='...'/>");
IconRegistry.RegisterProvider(new MyIconProvider()); // implements IIconProvider
```

---

## Key Interfaces (Tempo.Blazor.Abstractions)

These interfaces have zero UI dependencies — safe to reference from API/backend projects.

### Data Providers

```csharp
// Server-side data table
public interface IDataTableDataProvider<TItem>
{
    Task<PagedResult<TItem>> GetDataAsync(DataTableQuery query, CancellationToken ct = default);
}

// Async dropdown search
public interface IDropdownDataProvider<TItem>
{
    Task<DropdownDataResult<TItem>> GetItemsAsync(DropdownSearchRequest request, CancellationToken ct = default);
}

// Gallery images
public interface IImageGalleryDataProvider
{
    Task<IReadOnlyList<IGalleryImage>> GetImagesAsync(string? entityId = null, CancellationToken ct = default);
    Task<string> GetImageTicketUrlAsync(string imageId, CancellationToken ct = default);
    Task DeleteImageAsync(string imageId, CancellationToken ct = default);
}

// File attachments with chunked upload
public interface IFileAttachmentProvider
{
    Task<IReadOnlyList<IFileAttachment>> GetAttachmentsAsync(string entityId, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(string attachmentId, CancellationToken ct = default);
    Task DeleteAttachmentAsync(string attachmentId, CancellationToken ct = default);
    Task<string?> UploadChunkAsync(FileChunkData chunk, CancellationToken ct = default);
}

// Saved table views
public interface IDataTableViewProvider
{
    Task<IEnumerable<DataTableView>> GetViewsAsync(string viewContext, string? tenantId = null, string? userId = null, CancellationToken ct = default);
    Task<DataTableView> SaveViewAsync(string viewContext, DataTableView view, string? tenantId = null, string? userId = null, CancellationToken ct = default);
    Task DeleteViewAsync(string viewContext, string viewId, CancellationToken ct = default);
    Task<DataTableView?> GetDefaultViewAsync(string viewContext, string? tenantId = null, string? userId = null, CancellationToken ct = default);
    Task SetDefaultViewAsync(string viewContext, string viewId, string? tenantId = null, string? userId = null, CancellationToken ct = default);
}

// Dashboard persistence
public interface IDashboardProvider
{
    Task<IEnumerable<DashboardConfig>> GetDashboardsAsync(string? userId = null, CancellationToken ct = default);
    Task<DashboardConfig?> GetDashboardAsync(string dashboardId, CancellationToken ct = default);
    Task<DashboardConfig?> GetDefaultDashboardAsync(string? userId = null, CancellationToken ct = default);
    Task<DashboardConfig> SaveDashboardAsync(DashboardConfig dashboard, CancellationToken ct = default);
    Task DeleteDashboardAsync(string dashboardId, CancellationToken ct = default);
    Task SetDefaultDashboardAsync(string dashboardId, string? userId = null, CancellationToken ct = default);
}

// Widget registry
public interface IWidgetRegistry
{
    IEnumerable<WidgetDefinition> GetAllWidgets();
    WidgetDefinition? GetWidget(string widgetId);
    IEnumerable<WidgetDefinition> GetWidgetsByCategory(string category);
    void RegisterWidget(WidgetDefinition widget);
    IEnumerable<WidgetCategory> GetCategories();
}
```

### Entity Interfaces

```csharp
public interface ISelectOption<TValue>   { TValue Value; string Label; bool IsDisabled; string? Icon; }
public interface IDropdownItem<TValue> : ISelectOption<TValue> { string? Description; string? AvatarSrc; string? AvatarInitials; }
public interface ISidebarNavItem         { string Id; string Label; string Icon; string Href; int? BadgeCount; bool IsActive; IReadOnlyList<ISidebarNavItem>? Children; }
public interface IBreadcrumbItem         { string Label; string? Href; string? Icon; }
public interface ICommandPaletteAction   { string Id; string Title; string? Description; string? Icon; string? Shortcut; string? Category; Func<Task> Execute; }
public interface ITreeNode<TKey>         { TKey Id; string Label; string? Icon; bool IsLeaf; bool IsLoading; IReadOnlyList<ITreeNode<TKey>> Children; }
public interface ITag                    { string Id; string Name; string Color; }
public interface IStepItem               { string Id; string Label; string? Description; string? Icon; }
public interface IUserInfo               { string Id; string DisplayName; string? AvatarSrc; string? Email; }
public interface IGalleryImage           { string Id; string? Url; string? ThumbnailUrl; string? Title; string? Description; IEnumerable<string> Tags; DateTime? UploadedAt; string? UploadedBy; long? FileSizeBytes; }
public interface IFileAttachment         { string Id; string FileName; string ContentType; long FileSizeBytes; DateTimeOffset UploadedAt; string? UploadedByName; bool CanDelete; bool IsImage; }
public interface ITimelineEntry          { string Id; string EntryType; string AuthorName; string? AuthorAvatarUrl; DateTimeOffset CreatedAt; string? HtmlContent; string? PlainContent; bool IsInternal; IReadOnlyDictionary<string, string>? Metadata; }
public interface ICommentEntry           { string Id; string AuthorName; string? AuthorAvatarUrl; DateTimeOffset CreatedAt; DateTimeOffset? UpdatedAt; string HtmlContent; bool CanEdit; bool CanDelete; }
public interface INotificationItem       { string Id; string Title; string? Body; DateTimeOffset CreatedAt; bool IsRead; string? IconName; NotificationSeverity Severity; string? ActionUrl; }
public interface IMentionUser            { string Id; string UserName; string DisplayName; string? AvatarUrl; }
public interface IMentionDataProvider    { Task<IEnumerable<IMentionUser>> SearchUsersAsync(string query, CancellationToken ct = default); }
public interface IMultiViewListItem      { string Id; string Title; string? SubTitle; string? AvatarUrl; IReadOnlyList<ITag>? Tags; string? StatusLabel; string? StatusColor; DateTimeOffset? Date; }
public interface IImageUrlResolver       { Task<string> ResolveAsync(string imageId, CancellationToken ct = default); }
public interface IIconProvider           { string? GetSvg(string iconName); bool HasIcon(string iconName); }
```

### Key Models

```csharp
// Table query / result
record DataTableFilter(string Column, string Operator, object? Value);
class DataTableQuery { int Page; int PageSize; string? SortColumn; bool SortDescending; IReadOnlyList<DataTableFilter> Filters; string? SearchText; }
class PagedResult<T> { IReadOnlyList<T> Items; int TotalCount; int Page; int PageSize; int TotalPages; bool HasPreviousPage; bool HasNextPage; }

// Dropdown
class DropdownSearchRequest { string SearchText; int Page; int PageSize; IReadOnlyCollection<string> ExcludedIds; }
class DropdownDataResult<T> { IReadOnlyList<T> Items; int TotalCount; bool HasMore; static Empty(); static WithItems(items, totalCount); static WithAllItems(items); }

// Options
record SelectOption<TValue> : ISelectOption<TValue> { TValue Value; string Label; bool IsDisabled; string? Icon; static From(value, label); }
record DropdownItem<TValue> : IDropdownItem<TValue> { /* extends SelectOption + Description, AvatarSrc, AvatarInitials */ }

// Files
record FileChunkData(string FileName, string ContentType, long TotalSize, int ChunkIndex, int TotalChunks, byte[] Data, string? EntityId = null) { bool IsLast; }

// Dates
record DateRangePreset(string Label, DateOnly Start, DateOnly End);
static class DateRangePresets { Today; LastWeek; LastMonth; LastQuarter; ThisYear; }

// Filters
class FilterDefinition { string FieldName; string FieldLabel; FilterFieldType FieldType; IReadOnlyList<SelectOption<string>>? Options; }
record ActiveFilter(string FieldName, string FieldLabel, FilterOperator Operator, object? Value, string DisplayValue);
enum FilterFieldType { Text, Number, Date, DateTime, Boolean, Select, MultiSelect }
enum FilterOperator { Contains, NotContains, Equals, NotEquals, GreaterThan, LessThan, GreaterOrEqual, LessOrEqual, Between, IsEmpty, IsNotEmpty, In, NotIn }

// Dynamic forms
enum DynamicFieldType { Text, TextArea, Number, Date, DateTime, Time, Checkbox, Select }
record DynamicFieldDefinition { string Name; DynamicFieldType FieldType; string Label; string? Placeholder; bool Required; bool Disabled; IReadOnlyList<SelectOption<string>>? Options; string? DefaultValue; string? HelpText; }

// Dashboard
class DashboardConfig { string Id; string Name; bool IsDefault; GridConfig Grid; List<WidgetInstance> Widgets; }
class WidgetDefinition { string Id; string Name; string? Description; string Category; string Icon; int DefaultWidth; int DefaultHeight; int MinWidth; int MinHeight; bool IsResizable; bool AllowMultiple; string ComponentType; }
class WidgetInstance { string InstanceId; string WidgetId; string? CustomTitle; int X; int Y; int Width; int Height; string? ConfigJson; }

// Workflow
class WorkflowCanvasDefinition { List<CanvasState> States; List<CanvasTransition> Transitions; }
class CanvasState { string Id; string Name; CanvasStateType Type; double X; double Y; string? Color; }
enum CanvasStateType { Initial, Intermediate, Final }
class CanvasTransition { string Id; string? Label; string FromStateId; string ToStateId; string? Color; }

// Navigation
record SidebarNavItem : ISidebarNavItem { string Id; string Label; string Icon; string Href; int? BadgeCount; bool IsActive; IReadOnlyList<ISidebarNavItem>? Children; }
record BreadcrumbItem : IBreadcrumbItem { string Label; string? Href; string? Icon; }
```

---

## Localization

All user-facing text uses `ITmLocalizer`. The library ships with English and Czech translations.

```csharp
// Custom localizer
public class MyLocalizer : ITmLocalizer
{
    public string this[string key] => /* your lookup */;
    public string this[string key, params object[] arguments] => string.Format(this[key], arguments);
}

// Register after AddTempoBlazor()
builder.Services.AddSingleton<ITmLocalizer, MyLocalizer>();
```

Key prefixes: `Tm_*` (shared), `TmButton_*`, `TmDataTable_*`, `TmDatePicker_*`, `TmFileDropZone_*`, `TmRichEditor_*`, etc. See `TmResources.resx` for the full key list.

---

## CSS Design Tokens

Override via CSS custom properties:

```css
:root {
    /* Colors */
    --tm-color-primary: #3b82f6;
    --tm-color-primary-hover: #2563eb;
    --tm-color-danger: #ef4444;
    --tm-color-success: #22c55e;
    --tm-color-warning: #f59e0b;

    /* Typography */
    --tm-font-sans: 'Inter', system-ui, sans-serif;
    --tm-font-mono: 'JetBrains Mono', monospace;
    --tm-font-size-md: 0.875rem;

    /* Spacing */
    --tm-space-1: 0.25rem;  --tm-space-2: 0.5rem;  --tm-space-4: 1rem;  --tm-space-8: 2rem;

    /* Borders & Shadows */
    --tm-radius-md: 0.375rem;
    --tm-shadow-md: 0 4px 6px -1px rgb(0 0 0 / 0.1);

    /* Transitions */
    --tm-transition-fast: 150ms ease;
    --tm-transition-normal: 200ms ease;
}
```

Dark mode is activated by `[data-theme="dark"]` or `.tm-dark` on a parent element.

---

## Common Patterns

### Two-way binding
Most value components support `@bind-Value`:
```razor
<TmTextInput @bind-Value="name" />
<TmDatePicker @bind-Value="date" />
<TmSelect TValue="int" @bind-Value="selectedId" Options="options" />
```

### Data provider pattern
Server-side components accept a `DataProvider` or `Provider` parameter implementing an interface from `Tempo.Blazor.Abstractions`:
```csharp
// Implement the interface
public class MyDataProvider : IDataTableDataProvider<Person>
{
    public async Task<PagedResult<Person>> GetDataAsync(DataTableQuery query, CancellationToken ct)
    {
        // Your data access logic
    }
}

// Register in DI
builder.Services.AddScoped<IDataTableDataProvider<Person>, MyDataProvider>();
```

### Generic components
Some components require explicit type parameters in Razor:
```razor
<TmDataTable TItem="Person" ...>
    <TmDataTableColumn TItem="Person" ... />
</TmDataTable>

<TmTreeView TKey="string" ... />
<TmSelect TValue="int" ... />
<TmKanbanBoard TItem="TaskItem" ... />
<TmEntityPicker TItem="User" TValue="int" ... />
```

### FluentValidation with EditForm
```razor
<EditForm Model="model" OnValidSubmit="Submit">
    <FluentValidationValidator />
    <TmFormSection Title="Details">
        <TmFormRow>
            <TmFormField Label="Name" Required>
                <TmTextInput @bind-Value="model.Name" />
                <ValidationMessage For="() => model.Name" />
            </TmFormField>
        </TmFormRow>
    </TmFormSection>
    <TmButton Type="ButtonType.Submit">Save</TmButton>
</EditForm>
```

### Theme toggle
```csharp
@inject ThemeService ThemeService

<div data-theme="@ThemeService.ThemeName">
    <TmButton OnClick="ThemeService.Toggle">
        @(ThemeService.IsDark ? "Light mode" : "Dark mode")
    </TmButton>
</div>
```
