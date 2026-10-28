using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>Dialog for importing tasks from Excel, MS Project XML, or JIRA.</summary>
public partial class TmGanttImportDialog : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/Components/Scheduler/TmGanttImportDialog.razor.js";

    private enum ImportTab { Excel, Mpp, Jira }

    private static readonly ImportTab[] Tabs = [ImportTab.Excel, ImportTab.Mpp, ImportTab.Jira];

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private ImportTab _tab = ImportTab.Excel;
    private string? _selectedFileName;
    private IBrowserFile? _selectedFile;
    private string _jiraUrl = string.Empty;
    private string _jiraToken = string.Empty;
    private string _jiraProject = string.Empty;
    private string? _errorMessage;
    private bool _isImporting;
    private ElementReference _fileInputWrap;
    private ElementReference _triggerWrapRef;
    private IJSObjectReference? _filePickerModule;
    private readonly Dictionary<ImportTab, ElementReference> _tabRefs = [];
    private bool _focusSelectedTabAfterRender;
    private bool _wasOpen;
    private ElementReference _tabListRef;
    private Guid _inputKey = Guid.NewGuid();
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    /// <summary>Whether the dialog is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Fires when import completes successfully.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TmWorkItem>> OnImportCompleted { get; set; }

    /// <summary>Fires when import fails with an error message.</summary>
    [Parameter] public EventCallback<string> OnImportError { get; set; }

    /// <summary>Fires when the dialog should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // A dialog that is reopened starts from scratch: the previous file, error, busy flag and tab must not leak
        // into the next import.
        if (IsOpen && !_wasOpen)
        {
            ResetFile();
            _errorMessage = null;
            _isImporting = false;
            _tab = ImportTab.Excel;
        }

        _wasOpen = IsOpen;
    }

    private string TabId(ImportTab tab) => $"gantt-import-tab-{_instanceId}-{tab.ToString().ToLowerInvariant()}";

    private string PanelId => $"gantt-import-panel-{_instanceId}";

    private static string TabIcon(ImportTab tab) => tab switch
    {
        ImportTab.Excel => "table",
        ImportTab.Mpp => "calendar",
        _ => "link",
    };

    private static string TabLabelKey(ImportTab tab) => tab switch
    {
        ImportTab.Excel => "TmGantt_ImportExcel",
        ImportTab.Mpp => "TmGantt_ImportMpp",
        _ => "TmGantt_ImportJira",
    };

    /// <summary>The file extensions the active tab accepts; one string feeds both the input and the hint.</summary>
    private string SupportedFormats => _tab == ImportTab.Excel ? ".xlsx, .xls" : ".xml, .mpp";

    private void SelectTab(ImportTab tab)
    {
        if (tab == _tab)
        {
            _focusSelectedTabAfterRender = true;
            return;
        }

        _tab = tab;
        _focusSelectedTabAfterRender = true;
        _errorMessage = null;
        // A chosen .xlsx must never be handed to the MS Project importer (or the other way round), so a tab
        // switch starts from no file. The recreated input also clears the native value.
        ResetFile();
    }

    /// <summary>ARIA tablist keys: Left / Right wrap, Home / End jump; selection and DOM focus move together.</summary>
    private void HandleTabKeyDown(KeyboardEventArgs e)
    {
        var current = Array.IndexOf(Tabs, _tab);
        var target = e.Key switch
        {
            "ArrowRight" => (current + 1) % Tabs.Length,
            "ArrowLeft" => (current - 1 + Tabs.Length) % Tabs.Length,
            "Home" => 0,
            "End" => Tabs.Length - 1,
            _ => current,
        };

        if (target == current) return;
        SelectTab(Tabs[target]);
    }

    private void ResetFile()
    {
        _selectedFile = null;
        _selectedFileName = null;
        _inputKey = Guid.NewGuid();
    }

    private void RemoveFile()
    {
        ResetFile();
        _errorMessage = null;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Cold start: import the focus-trap module and install its document-level opener
            // records NOW, so the FIRST open of the session restores its opener.
            await Tempo.Blazor.Helpers.FocusTrap.EnsureLoadedAsync(JS);
        }

        if (_focusSelectedTabAfterRender)
        {
            _focusSelectedTabAfterRender = false;
            if (IsOpen && _tabRefs.TryGetValue(_tab, out var tab))
            {
                try
                {
                    await tab.FocusAsync();
                }
                catch (InvalidOperationException)
                {
                    // The tab left the DOM with the dialog.
                }
            }
        }

        // The tablist's Home / End / Arrow keys must not also scroll the dialog body (a Blazor handler cannot
        // preventDefault conditionally); the JS marker dedupes per element, so re-attempting on every render is free.
        if (IsOpen)
        {
            _filePickerModule ??= await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await _filePickerModule.InvokeVoidAsync("registerTablistKeys", _tabListRef);
        }

        // Register the NATIVE click listener ahead of the first click - re-attempted on EVERY
        // render while the trigger element exists, because tab switches and "Remove file" destroy and
        // recreate the file area: a C# "registered once" flag can never see the fresh button, but the JS
        // dataset.tmFilePickerRegistered marker dedupes per element so repeat calls on the same element
        // are no-ops. input.click() then runs inside the browser's own click event with no interop in
        // the gesture chain (N204, review 2026-09-22).
        if (IsOpen && _tab is ImportTab.Excel or ImportTab.Mpp)
        {
            _filePickerModule ??= await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await _filePickerModule.InvokeVoidAsync(
                "registerFilePickerTrigger", _triggerWrapRef, _fileInputWrap);
        }
    }

    private void OnFileChangedAsync(InputFileChangeEventArgs e)
    {
        _selectedFile = e.File;
        _selectedFileName = e.File.Name;
        _errorMessage = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_filePickerModule is not null)
            {
                await _filePickerModule.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit already gone.
        }
    }

    /// <summary>
    /// The Import action needs something to import: a chosen file on the Excel / MS Project tabs, or the
    /// URL, token and project key on the JIRA tab. It used to stay enabled and silently return.
    /// </summary>
    private bool CanImport => !_isImporting && _tab switch
    {
        ImportTab.Jira => !string.IsNullOrWhiteSpace(_jiraUrl)
                          && !string.IsNullOrWhiteSpace(_jiraToken)
                          && !string.IsNullOrWhiteSpace(_jiraProject),
        _ => _selectedFile is not null,
    };

    private async Task ImportAsync()
    {
        if (!CanImport) return;
        _errorMessage = null;
        _isImporting = true;
        try
        {
            IReadOnlyList<TmWorkItem> tasks;
            if (_tab == ImportTab.Excel)
            {
                if (ResolveGanttExcelImporterType() is not { } importerType)
                {
                    // Already-localized guidance - this path never reaches the exception
                    // classifier below (it is a supported configuration state, not a failure).
                    _errorMessage = Loc["TmGantt_ImportError"].Replace("{0}", Loc["TmGantt_ImportXlsxPackageMissing"]);
                    await OnImportError.InvokeAsync(_errorMessage);
                    return;
                }
                using var stream = _selectedFile!.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
                tasks = ImportExcelTasks(stream, importerType);
            }
            else if (_tab == ImportTab.Mpp)
            {
                using var stream = _selectedFile!.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
                tasks = GanttMppImporter.Import(stream);
            }
            else
            {
                using var importer = new GanttJiraImporter(new HttpClient());
                tasks = await importer.ImportAsync(_jiraUrl, _jiraToken, _jiraProject);
            }
            await OnImportCompleted.InvokeAsync(tasks);
            await OnClose.InvokeAsync();
        }
        catch (Exception ex)
        {
            _errorMessage = Loc["TmGantt_ImportError"].Replace("{0}", ImportErrorDetail(ex));
            await OnImportError.InvokeAsync(_errorMessage);
        }
        finally
        {
            _isImporting = false;
        }
    }

    /// <summary>
    /// Maps an import failure to a LOCALIZED detail - never <see cref="Exception.Message"/>:
    /// framework messages are English, machine-specific, and can carry file paths, URLs, or
    /// inner-exception details the dialog must not render (review 2026-09-22 carry-forward).
    /// </summary>
    private string ImportErrorDetail(Exception exception) => exception switch
    {
        GanttImportAuthException => Loc["TmGantt_ImportErrorAuth"],
        HttpRequestException => Loc["TmGantt_ImportErrorNetwork"],
        IOException or InvalidDataException => Loc["TmGantt_ImportErrorFile"],
        _ => Loc["TmGantt_ImportErrorGeneric"],
    };

    private IReadOnlyList<TmWorkItem> ImportExcelTasks(Stream stream, Type importerType)
    {
        var importMethod = importerType.GetMethods()
            .FirstOrDefault(method =>
            {
                if (method.Name != "Import")
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 2 && parameters[0].ParameterType == typeof(Stream);
            })
            ?? throw new MissingMethodException(importerType.FullName, "Import");

        return importMethod.Invoke(null, [stream, null]) as IReadOnlyList<TmWorkItem>
            ?? Array.Empty<TmWorkItem>();
    }

    private static Type? ResolveGanttExcelImporterType()
        => Type.GetType("Tempo.Blazor.Services.GanttExcelImporter, Tempo.Blazor.GanttXlsx")
           ?? AppDomain.CurrentDomain.GetAssemblies()
               .Select(assembly => assembly.GetType("Tempo.Blazor.Services.GanttExcelImporter", throwOnError: false))
               .FirstOrDefault(type => type is not null);
}
