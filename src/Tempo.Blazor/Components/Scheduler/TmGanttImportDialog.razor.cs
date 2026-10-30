using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Dialog for importing tasks from Excel, MS Project XML, or JIRA. The Excel flow of a result-capable
/// GanttXlsx package reads the sheet's headers first, confirms the column mapping in the
/// <see cref="TmGanttImportMappingDialog"/> and then imports the full result (tasks, dependencies,
/// warnings); an older package keeps today's direct import without the mapping step.
/// </summary>
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

    // The mapping step: the chosen file's headers, the open mapping dialog and the finished result
    // whose warnings the alert below keeps readable until the user closes the dialog.
    private IReadOnlyList<string> _headers = [];
    private bool _mappingDialogOpen;
    private GanttImportResult? _completedResult;

    /// <summary>Whether the dialog is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Fires when import completes successfully (tasks only; the full result travels through <see cref="OnImportResultCompleted"/>).</summary>
    [Parameter] public EventCallback<IReadOnlyList<TmWorkItem>> OnImportCompleted { get; set; }

    /// <summary>
    /// Fires when a result-capable Excel import finishes: tasks, dependencies and warnings in one
    /// payload. When handled, this event replaces the task-only <see cref="OnImportCompleted"/> for
    /// the Excel flow — MS Project and JIRA keep firing the task-only event.
    /// </summary>
    [Parameter] public EventCallback<GanttImportResult> OnImportResultCompleted { get; set; }

    /// <summary>Fires when import fails with an error message.</summary>
    [Parameter] public EventCallback<string> OnImportError { get; set; }

    /// <summary>Fires when the dialog should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// Mappings the column-mapping dialog starts from (a remembered choice, a host's own defaults);
    /// forwarded to <see cref="TmGanttImportMappingDialog.InitialMappings"/>.
    /// </summary>
    [Parameter] public IReadOnlyList<GanttColumnMapping>? ImportMappings { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // A dialog that is reopened starts from scratch: the previous file, error, busy flag, tab,
        // mapping step and warnings must not leak into the next import.
        if (IsOpen && !_wasOpen)
        {
            ResetFile();
            _errorMessage = null;
            _isImporting = false;
            _tab = ImportTab.Excel;
            _headers = [];
            _mappingDialogOpen = false;
            _completedResult = null;
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
        _mappingDialogOpen = false;
        _completedResult = null;
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
        _completedResult = null;
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
        _completedResult = null;
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

                if (ResolveExcelResultApi(importerType) is not { } api)
                {
                    // An older GanttXlsx: today's direct import, no mapping step, no result payload.
                    await using var legacyStream = await BufferedAsync(_selectedFile!, 10 * 1024 * 1024);
                    var legacyTasks = ImportExcelTasks(legacyStream, importerType);
                    await CompleteLegacyImportAsync(legacyTasks);
                    return;
                }

                // Result-capable: read the headers, then confirm the mapping before any import. The
                // upload is buffered into a MemoryStream first: OpenXml seeks the stream, which the
                // browser's upload stream does not support.
                await using (var headersStream = await BufferedAsync(_selectedFile!, 10 * 1024 * 1024))
                {
                    _headers = await api.ReadHeadersAsync(headersStream);
                }

                _completedResult = null;
                _mappingDialogOpen = true;
                return;
            }

            IReadOnlyList<TmWorkItem> tasks;
            if (_tab == ImportTab.Mpp)
            {
                await using var stream = _selectedFile!.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
                tasks = GanttMppImporter.Import(stream);
            }
            else
            {
                using var importer = new GanttJiraImporter(new HttpClient());
                tasks = await importer.ImportAsync(_jiraUrl, _jiraToken, _jiraProject);
            }

            await CompleteLegacyImportAsync(tasks);
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
    /// The chosen file as a seekable <see cref="MemoryStream"/>: OpenXml seeks the stream, and the
    /// browser's upload stream (the only thing <c>InputFile</c> hands out) does not support that.
    /// </summary>
    private static async Task<MemoryStream> BufferedAsync(IBrowserFile file, long maxAllowedSize)
    {
        var buffered = new MemoryStream();
        await using var upload = file.OpenReadStream(maxAllowedSize);
        await upload.CopyToAsync(buffered);
        buffered.Position = 0;
        return buffered;
    }

    /// <summary>The task-only completion of the MS Project / JIRA flows and of a legacy GanttXlsx package.</summary>
    private async Task CompleteLegacyImportAsync(IReadOnlyList<TmWorkItem> tasks)
    {
        await OnImportCompleted.InvokeAsync(tasks);
        await OnClose.InvokeAsync();
    }

    /// <summary>The mapping dialog confirmed: run the import with the chosen mapping.</summary>
    private async Task ImportExcelResultAsync(IReadOnlyList<GanttColumnMapping> mappings)
    {
        if (ResolveGanttExcelImporterType() is not { } importerType
            || ResolveExcelResultApi(importerType) is not { } api)
        {
            return;
        }

        _errorMessage = null;
        _isImporting = true;
        try
        {
            await using var stream = await BufferedAsync(_selectedFile!, 10 * 1024 * 1024);
            // A fresh prefix per import run: importing the same sheet twice must never mint
            // colliding task ids (the default prefix is only stable for one import).
            var options = new GanttExcelImportOptions
            {
                IdPrefix = $"xlsx-{Guid.NewGuid().ToString("N")[..8]}-row-",
            };
            var result = await api.ImportResultAsync(stream, mappings, options);
            _mappingDialogOpen = false;

            if (OnImportResultCompleted.HasDelegate)
            {
                // The host owns the completion (and, on warnings, keeps this dialog open so the
                // alert below stays readable). Stash the result BEFORE raising so the alert exists
                // as soon as the host's handler returns, and drop the chosen file: the footer now
                // offers Close only, and nothing may re-import the same file from this dialog.
                _completedResult = result;
                ResetFile();
                await OnImportResultCompleted.InvokeAsync(result);
                if (result.Warnings.Count == 0)
                {
                    await OnClose.InvokeAsync();
                }
            }
            else
            {
                // A host that only knows the task-only event keeps today's contract verbatim.
                _completedResult = null;
                await OnImportCompleted.InvokeAsync(result.Tasks);
                await OnClose.InvokeAsync();
            }
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
    /// Localized text of one warning — the code and its data are translated here, never in the model.
    /// The row in the message is the SHEET row (<see cref="GanttImportWarning.SheetRow"/>, the header
    /// row is 1) — the number the user sees in Excel; <see cref="GanttImportWarning.Row"/> stays the
    /// data ordinal the predecessor references are built on.
    /// </summary>
    private string WarningText(GanttImportWarning warning) => warning.Code switch
    {
        GanttImportWarningCode.UnparsedValue => Loc["TmGantt_ImportWarningUnparsedValue", warning.SheetRow, warning.Column ?? "?", warning.Value ?? string.Empty],
        _ => Loc[$"TmGantt_ImportWarning{warning.Code}", warning.SheetRow, warning.Value ?? string.Empty],
    };

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

    /// <summary>
    /// The result-capable surface of the GanttXlsx importer, resolved reflectively: the full
    /// <c>ImportResult</c> plus <c>ReadHeaders</c>. Null on an older package — the caller falls
    /// back to the plain <c>Import</c> and no mapping step.
    /// </summary>
    private static ExcelImportApi? ResolveExcelResultApi(Type importerType)
    {
        var importResult = importerType.GetMethods().FirstOrDefault(method =>
        {
            if (method.Name != "ImportResult") return false;
            var parameters = method.GetParameters();
            return parameters.Length >= 2
                && parameters[0].ParameterType == typeof(Stream)
                && typeof(System.Collections.IEnumerable).IsAssignableFrom(parameters[1].ParameterType);
        });

        var readHeaders = importerType.GetMethods().FirstOrDefault(method =>
        {
            if (method.Name != "ReadHeaders") return false;
            var parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == typeof(Stream);
        });

        return importResult is not null && readHeaders is not null
            ? new ExcelImportApi(importResult, readHeaders)
            : null;
    }

    private sealed record ExcelImportApi(MethodInfo ImportResultMethod, MethodInfo ReadHeadersMethod)
    {
        public Task<IReadOnlyList<string>> ReadHeadersAsync(Stream stream) => Task.FromResult(
            ReadHeadersMethod.Invoke(null, [stream]) as IReadOnlyList<string> ?? []);

        public Task<GanttImportResult> ImportResultAsync(Stream stream,
            IReadOnlyList<GanttColumnMapping> mappings, GanttExcelImportOptions options) => Task.FromResult(
            ImportResultMethod.Invoke(null, BuildImportArgs(stream, mappings, options)) as GanttImportResult
            ?? new GanttImportResult());

        /// <summary>
        /// Fills the importer's parameter list: an older result-capable package takes
        /// (stream, mappings) only, the current one takes the options as a third parameter.
        /// </summary>
        private object?[] BuildImportArgs(Stream stream, IReadOnlyList<GanttColumnMapping> mappings,
            GanttExcelImportOptions options)
        {
            var parameters = ImportResultMethod.GetParameters();
            var args = new object?[parameters.Length];
            args[0] = stream;
            args[1] = mappings;
            if (parameters.Length > 2) args[2] = options;
            return args;
        }
    }

    private static Type? ResolveGanttExcelImporterType()
        => Type.GetType("Tempo.Blazor.Services.GanttExcelImporter, Tempo.Blazor.GanttXlsx")
           ?? AppDomain.CurrentDomain.GetAssemblies()
               .Select(assembly => assembly.GetType("Tempo.Blazor.Services.GanttExcelImporter", throwOnError: false))
               .FirstOrDefault(type => type is not null);
}
