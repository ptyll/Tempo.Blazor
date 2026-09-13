# Tempo.Blazor E2E Tests

This project contains end-to-end tests for the Tempo.Blazor component library using Playwright and MSTest.

## Test Coverage

### Server Rendering Tests (`ServerRenderingTests.cs`)
- Basic component rendering (TmButton, TmCard, TmBadge, TmAlert)
- Form inputs rendering (TmInput, TmSelect, TmCheckbox)
- Dark mode toggle functionality
- Localization switching (CZ/EN)
- Page navigation without errors

### InteractiveAuto Tests (`InteractiveAutoTests.cs`)
- Prerendering without errors
- WASM boot and hydration
- Rich Editor rendering after WASM boot
- Dashboard drag & drop functionality
- Workflow Designer rendering
- Scheduler views (Month, Week, Day, Timeline)
- DataTable client-side data handling
- Memory leak detection

### WASM Tests (`InteractiveAutoTests.cs` - `WasmTests` class)
- WASM app loading and rendering

## Prerequisites

1. Install Playwright browsers:
```bash
dotnet tool install --global Microsoft.Playwright.CLI
playwright install
```

2. Start the demo applications:
```bash
# Terminal 1 - Start WASM Demo
dotnet run --project src/Tempo.Blazor.Demo

# Terminal 2 - Start Server Demo
dotnet run --project src/Tempo.Blazor.Demo.Server

# Terminal 3 - Start InteractiveAuto Demo
dotnet run --project src/Tempo.Blazor.Demo.InteractiveAuto/Tempo.Blazor.Demo.InteractiveAuto
```

## Running Tests

### Run all tests
```bash
dotnet test tests/Tempo.Blazor.E2E/
```

### Run tests by category
```bash
# Server tests only
dotnet test tests/Tempo.Blazor.E2E/ --filter "Category=Server"

# WASM tests only
dotnet test tests/Tempo.Blazor.E2E/ --filter "Category=WASM"

# InteractiveAuto tests only
dotnet test tests/Tempo.Blazor.E2E/ --filter "Category=InteractiveAuto"
```

### Run specific test
```bash
dotnet test tests/Tempo.Blazor.E2E/ --filter "FullyQualifiedName~TmButton_Renders"
```

### Run with UI (headed mode)
Set the `Headless` property to `false` in the test context or modify the `ClassInitialize` method.

## Test URLs

| Application | URL |
|------------|-----|
| WASM Demo | https://localhost:7106 |
| Server Demo | https://localhost:7107 |
| InteractiveAuto Demo | https://localhost:7108 |

## Test Architecture

```
PlaywrightTestBase (abstract)
    ├── WasmTestBase (BaseUrl: https://localhost:7106)
    │   └── WasmTests
    ├── ServerTestBase (BaseUrl: https://localhost:7107)
    │   └── ServerRenderingTests
    └── InteractiveAutoTestBase (BaseUrl: https://localhost:7108)
        └── InteractiveAutoTests
```

## Screenshot baselines

Baseline PNGs are **run artefacts, not source**. Since 2.8.26 the repository holds none:
`tests/Tempo.Blazor.E2E/__baseline__/` must stay empty, and `BaselineWriteSweep` fails the run if a
PNG ever appears there — staged, indexed, or merely on disk.

- Ordinary captures write to the run's TestResults directory via `BaselineOutput.DirectoryFor`.
- Regenerating baselines on purpose writes to `artifacts/baseline/` (gitignored):

```bash
TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E \
    --filter "TestCategory=BaselineGeneration"
```

- Review or publish the PNGs from `artifacts/baseline/` directly; they are never committed.

## Adding New Tests

1. Create a new test class inheriting from the appropriate base class:
```csharp
[TestClass]
public class MyNewTests : ServerTestBase
{
    [TestMethod]
    public async Task MyTest()
    {
        var page = await CreatePageAsync();
        // Test code here
    }
}
```

2. Use the helper methods from `PlaywrightTestBase`:
- `CreatePageAsync()` - Creates a new page and navigates to base URL
- `NavigateToPageAsync(page, "Menu Text")` - Clicks navigation menu
- `ToggleDarkModeAsync(page)` - Toggles dark mode
- `SwitchLanguageAsync(page, "cs")` - Switches language
- `TakeScreenshotAsync(page, "name")` - Takes screenshot
- `GetHeapSizeAsync(page)` - Gets JS heap size

### Document Editor Tests

Document editor E2E tests should exercise the canvas editor host. Open `/document-editor` or a dedicated canvas route, wait for `[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']`, and prefer user-visible DOM/provider assertions plus canvas pixel checks over implementation-only probes.

Keep coverage spread across typing, undo/redo, formatting, track changes, comments, images, tables, headers/footers, collaboration, save/reload, import/export, PDF export, and comparison. Canvas E2E files are classified by `DocumentEditorCanvasParityCoverageMatrixTests`.

For local editor assertions, prefer `WaitForEditorStableAsync(page, reason, blockId, expectedText)` over fixed sleeps. The helper waits for the host, visible blocks, optional expected text and absence of Blazor/runtime error UI; it deliberately does not wait for save/autosave.

## CI/CD Integration

For CI/CD pipelines, use headless mode and ensure demo apps are running:

```yaml
# Example GitHub Actions step
- name: Run E2E Tests
  run: |
    dotnet run --project src/Tempo.Blazor.Demo &
    dotnet run --project src/Tempo.Blazor.Demo.Server &
    dotnet run --project src/Tempo.Blazor.Demo.InteractiveAuto/Tempo.Blazor.Demo.InteractiveAuto &
    sleep 30  # Wait for apps to start
    dotnet test tests/Tempo.Blazor.E2E/ --no-build
```

## Troubleshooting

### Browser not found
```bash
playwright install
```

### Connection refused
Ensure demo applications are running on the expected ports.

### Timeout errors
Increase timeout values in `PlaywrightTestBase.cs` or ensure apps are fully loaded before tests run.

### Screenshots not showing
Check `TestContext.TestResultsDirectory` for screenshot locations.
