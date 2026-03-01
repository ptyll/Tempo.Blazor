# Tempo.Blazor – AI Agent Guide

## Project Overview

**Tempo.Blazor** is a comprehensive Blazor component library with 100+ components designed for AI-assisted development. The library provides a complete UI toolkit for building modern Blazor WebAssembly applications with support for localization, theming (light/dark), FluentValidation integration, and a CSS design system based on custom properties.

### Key Features
- 100+ reusable Razor components (inputs, data tables, pickers, layout, feedback, charts, dashboards, workflow designer, etc.)
- Full localization support via `ITmLocalizer` (English + Czech built-in, extensible)
- CSS design system with CSS custom properties (`--tm-*` tokens)
- Dark mode support via `ThemeService`
- FluentValidation integration (optional separate package)
- Icon extensibility via `IconRegistry` and `IIconProvider`
- WCAG 2.1 AA accessibility compliance

## Technology Stack

| Category | Technology |
|----------|------------|
| Framework | .NET 10.0 |
| UI Framework | Blazor WebAssembly |
| Language | C# 12 (latest) |
| Styling | CSS Custom Properties (Design Tokens) |
| Validation | FluentValidation 12.1.1 |
| Localization | Microsoft.Extensions.Localization |
| Testing | xUnit + bUnit + Playwright (E2E) |

## Project Structure

```
TempoBlazor.slnx
├── src/
│   ├── Tempo.Blazor.Abstractions/    # Interfaces and models (NuGet package)
│   ├── Tempo.Blazor/                 # Main component library (NuGet package)
│   ├── Tempo.Blazor.FluentValidation/# Optional FluentValidation integration
│   ├── Tempo.Blazor.Demo/            # Blazor WASM demo application
│   ├── Tempo.Blazor.Demo.Shared/     # Shared DTOs between API and Demo
│   └── Tempo.Blazor.Demo.Api/        # ASP.NET Core Minimal API for demo data
├── tests/
│   ├── Tempo.Blazor.Tests/           # bUnit component tests
│   ├── Tempo.Blazor.E2E/             # Playwright end-to-end tests
│   ├── Tempo.Blazor.Demo.Api.Tests/  # API integration tests
│   └── Tempo.Blazor.FluentValidation.Tests/  # Validation tests
└── planning/                          # Sprint planning documents (Czech)
```

### Project Dependencies

```
Tempo.Blazor.Abstractions (no UI dependencies)
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

### Build
```bash
# Build entire solution
dotnet build TempoBlazor.slnx

# Build specific project
dotnet build src/Tempo.Blazor/Tempo.Blazor.csproj
```

### Test
```bash
# Run all tests
dotnet test

# Run with verbosity
dotnet test --verbosity normal

# Run specific test project
dotnet test tests/Tempo.Blazor.Tests/
```

### Package
```bash
# Create NuGet packages (auto-generated on Release build)
dotnet build -c Release

# Packages are created in:
# src/Tempo.Blazor.Abstractions/bin/Release/*.nupkg
# src/Tempo.Blazor/bin/Release/*.nupkg
# src/Tempo.Blazor.FluentValidation/bin/Release/*.nupkg
```

### Run Demo
```bash
# Start Demo API (terminal 1)
cd src/Tempo.Blazor.Demo.Api
dotnet run
# API runs on: https://localhost:5100

# Start Demo WASM (terminal 2)
cd src/Tempo.Blazor.Demo
dotnet run
# App runs on: https://localhost:7106
```

## Code Organization

### Component Structure

Components are organized by category in `src/Tempo.Blazor/Components/`:

| Category | Components |
|----------|------------|
| Activity | `TmActivityLog`, `TmActivityComments`, `TmActivityAttachments`, `TmActivityTimeline`, `TmRichEditorFull`, `TmRichEditorSimple` |
| Avatars | `TmAvatar`, `TmAvatarGroup` |
| Buttons | `TmButton`, `TmSplitButton`, `TmCopyButton` |
| Charts | `TmChart` (Bar, Line, Pie, Donut, HorizontalBar — pure SVG) |
| Dashboard | `TmDashboard`, `TmWidgetSelector` (drag & resize grid, JS interop) |
| DataDisplay | `TmBadge`, `TmCard`, `TmEmptyState`, `TmMultiViewList`, `TmStatCard`, `TmAccordion`, `TmAccordionItem`, `TmChip`, `TmChipGroup`, `TmKanbanBoard` |
| DataTable | `TmDataTable`, `TmDataTableColumn`, `TmColumnFilter`, `TmColumnPicker`, `TmPagination`, `TmViewManager`, `TmBulkActionBar` |
| Dropdowns | `TmDropdown`, `TmDropdownItem`, `TmFilterableDropdown` |
| Feedback | `TmNotificationBell`, `TmSkeleton`, `TmSpinner`, `TmAlert`, `TmDialog`, `TmModal`, `TmProgressBar`, `TmToastContainer`, `TmTooltip`, `TmPopover` |
| Files | `TmAttachmentManager`, `TmFileDropZone` |
| Filters | `TmFilterBuilder`, `TmFilterChip` |
| Forms | `TmFormField`, `TmFormRow`, `TmFormSection`, `TmValidationSummary`, `TmValidatedField`, `TmDynamicFormRenderer` |
| Gallery | `TmImageGallery`, `TmLightbox` |
| Icons | `TmIcon`, `IconRegistry`, `IIconProvider` |
| Inputs | `TmTextInput`, `TmTextArea`, `TmSelect`, `TmCheckbox`, `TmToggle`, `TmRadio`, `TmRadioGroup`, `TmSearchInput`, `TmPasswordStrengthIndicator`, `TmNumberInput`, `TmEntityPicker`, `TmExpressionEditor` |
| Layout | `TmSidebar`, `TmBreadcrumbs`, `TmTopBar`, `TmCommandPalette`, `TmDrawer` |
| Navigation | `TmTabs`, `TmTabPanel`, `TmContextMenu`, `TmContextMenuItem` |
| Notifications | `TmNotificationBell` (extended, per-item read, severity) |
| Pickers | `TmDatePicker`, `TmDateRangePicker`, `TmDateTimePicker`, `TmDateTimeRangePicker`, `TmTimePicker`, `TmTimeRangePicker`, `TmCalendarView` |
| Tags | `TmTagPicker` |
| Timeline | `TmTimeline` |
| Toolbar | `TmToolbar`, `TmToolbarButton`, `TmToolbarDivider` |
| TreeView | `TmTreeView` |
| Workflow | `TmStepper`, `TmWorkflowDesignerCanvas`, `TmWorkflowToolbox`, `TmWorkflowPropertiesPanel`, `TmWorkflowMinimap` |

### CSS Architecture

```
wwwroot/css/
├── tempo-blazor.css          # Main entry point with @imports
├── tokens.css                # Design tokens (colors, spacing, typography)
├── tokens-dark.css           # Dark mode token overrides
├── base.css                  # Reset and base styles
├── animations.css            # Keyframes and animation utilities
├── breakpoints.css           # Responsive breakpoints
└── components/               # Individual component styles
    ├── _button.css
    ├── _input.css
    ├── _data-table.css
    └── ... (43 files)
```

### Abstractions (Shared Library)

`Tempo.Blazor.Abstractions` contains zero-UI dependencies:
- **Interfaces**: `IDataTableDataProvider`, `IDropdownDataProvider`, `IFileAttachmentProvider`, etc.
- **Models**: `SelectOption`, `DropdownItem`, `DataTableView`, `PagedResult`, etc.
- **Localization**: `ITmLocalizer`

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
All user-visible strings must use localization:
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
    padding: var(--tm-spacing-2) var(--tm-spacing-4);
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
- Prefix: `Tm` (Tempo)
- Format: `Tm{ComponentName}.razor`
- Namespace: `Tempo.Blazor.Components.{Category}`

## Testing Strategy

### Unit Tests (bUnit)
Location: `tests/Tempo.Blazor.Tests/`

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

Test base class provides mocked localization:
```csharp
public class LocalizationTestBase : TestContext
{
    protected MockTmLocalizer MockLocalizer { get; }
    
    public LocalizationTestBase()
    {
        MockLocalizer = new MockTmLocalizer();
        Services.AddSingleton<ITmLocalizer>(MockLocalizer);
    }
}
```

### E2E Tests (Playwright)
Location: `tests/Tempo.Blazor.E2E/`

Uses MSTest runner (`EnableMSTestRunner=true`). Tests run against running Demo app.

### API Tests
Location: `tests/Tempo.Blazor.Demo.Api.Tests/`

Uses `Microsoft.AspNetCore.Mvc.Testing` for integration testing.

## Localization

### Resource Files
Location: `src/Tempo.Blazor/Resources/`
- `TmResources.resx` – English (default)
- `TmResources.cs.resx` – Czech

### Adding New Keys
1. Add to `TmResources.resx` (English)
2. Add to `TmResources.cs.resx` (Czech)
3. Use in component: `@Loc["KeyName"]`
4. Add to `MockTmLocalizer` for tests

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
// Program.cs
builder.Services.AddScoped<ThemeService>();

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

## Security Considerations

1. **XSS Prevention**: Components use `@` (encoded) output by default. Use `@((MarkupString)…)` only for trusted content.
2. **Icon SVGs**: Custom icons are rendered as `MarkupString`. Ensure SVG content is trusted/sanitized.
3. **No Secrets**: Demo API uses mock data stores with generated fake data.

## Sprint Planning

The `planning/` directory contains detailed sprint documentation (in Czech):
- Sprint 0: Abstractions + IconRegistry
- Sprint 1: Localization (no hardcoded texts)
- Sprint 2: DataTable full implementation
- Sprint 3: Activity components
- Sprint 4: Date/Time pickers (6 variants)
- Sprint 5: Missing components
- Sprint 6: Demo API
- Sprint 7: CSS Design System
- Sprint 8: FluentValidation
- Sprint 9: Playwright E2E tests
- Sprint 10: Missing components (Charts, Dashboard, Kanban, Accordion, Chip, Tabs, Context Menu, Drawer, Dialog, Modal, Alert, Toast, Progress Bar, Tooltip, Popover, Split Button, Copy Button, Number Input, Entity Picker, Expression Editor, Dynamic Form, Workflow Designer, Calendar View, Validated Field, Bulk Action Bar)

## JavaScript Interop

Three JS files in `wwwroot/js/` require `<script>` tags in `index.html` when using specific components:
- `dashboard.js` — required by `TmDashboard` (drag & resize grid)
- `workflow-designer.js` — required by `TmWorkflowDesignerCanvas` (SVG drag, pan, zoom, transition creation)
- `richEditor.js` — required by `TmRichEditorFull` / `TmRichEditorSimple` (contenteditable interop)

## AI Agent Documentation

The NuGet package includes `AGENT.md` with complete API reference for AI agents in consuming projects. This covers:
- All component parameters and usage examples
- All interfaces and models from `Tempo.Blazor.Abstractions`
- Setup instructions, CSS tokens, localization, and common patterns

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
```

## Language Note

Code, XML documentation, and comments are in **English**. Planning documents are in **Czech**. The library supports both English (`en`) and Czech (`cs`) localization out of the box.
