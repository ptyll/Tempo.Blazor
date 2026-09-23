using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>Dialog for importing tasks from Excel, MS Project XML, or JIRA.</summary>
public partial class TmGanttImportDialog : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/Components/Scheduler/TmGanttImportDialog.razor.js";

    private enum ImportTab { Excel, Mpp, Jira }

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
    private ElementReference _dialogElement;
    private IJSObjectReference? _filePickerModule;
    private bool _wasOpen;
    private bool _shouldActivateTrap;
    private FocusTrap? _focusTrap;
    private DotNetObjectReference<TmGanttImportDialog>? _dotNetRef;
    private readonly string _titleId = $"tm-gantt-import-title-{Guid.NewGuid():N}";

    /// <summary>Whether the dialog is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Fires when import completes successfully.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TmWorkItem>> OnImportCompleted { get; set; }

    /// <summary>Fires when import fails with an error message.</summary>
    [Parameter] public EventCallback<string> OnImportError { get; set; }

    /// <summary>Fires when the dialog should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// The dialog renders inline at the end of the Gantt DOM — behind hundreds of task-tree
    /// tab stops — so a keyboard user could never reach it by Tab alone. Moving focus inside
    /// (and trapping it there while open) is the standard modal contract TmModal/TmDialog
    /// already implement via the shared <see cref="FocusTrap"/>.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_wasOpen)
        {
            _wasOpen = true;
            _shouldActivateTrap = true;
        }
        else if (!IsOpen && _wasOpen)
        {
            _wasOpen = false;
            await DeactivateTrapAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldActivateTrap && IsOpen)
        {
            _shouldActivateTrap = false;
            _dotNetRef ??= DotNetObjectReference.Create(this);
            _focusTrap ??= new FocusTrap(JS);
            // Escape is handled at the document level so it closes the dialog regardless of
            // where focus currently sits — the modal contract TmDrawer/TmModal already follow
            // (carry-forward review item: the dialog trapped focus but ignored Escape).
            await _focusTrap.ActivateAsync<TmGanttImportDialog>(_dialogElement, _dotNetRef, closeOnEscape: true);
        }

        // Register the NATIVE click listener ahead of the first click — re-attempted on EVERY
        // render while the trigger element exists, because tab switches (@if/else-if in the
        // markup) destroy and recreate the file area: a C# "registered once" flag can never see
        // the fresh button, but the JS dataset.tmFilePickerRegistered marker dedupes per element
        // so repeat calls on the same element are no-ops. input.click() then runs inside the
        // browser's own click event with no interop in the gesture chain (N204, review 2026-09-22).
        if (IsOpen && _tab is ImportTab.Excel or ImportTab.Mpp)
        {
            _filePickerModule ??= await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await _filePickerModule.InvokeVoidAsync(
                "registerFilePickerTrigger", _triggerWrapRef, _fileInputWrap);
        }
    }

    private async Task DeactivateTrapAsync()
    {
        if (_focusTrap is not null)
        {
            await _focusTrap.DeactivateAsync();
        }
    }

    /// <summary>Invoked by the shared focus-trap module when Escape is pressed at the document level.</summary>
    [JSInvokable]
    public async Task HandleFocusTrapEscapeAsync()
    {
        await OnClose.InvokeAsync();
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
            if (_focusTrap is not null)
            {
                await _focusTrap.DisposeAsync();
            }
            if (_filePickerModule is not null)
            {
                await _filePickerModule.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit already gone.
        }
        finally
        {
            _dotNetRef?.Dispose();
        }
    }

    private async Task ImportAsync()
    {
        _errorMessage = null;
        _isImporting = true;
        try
        {
            IReadOnlyList<TmWorkItem> tasks;
            if (_tab == ImportTab.Excel)
            {
                if (_selectedFile is null) return;
                using var stream = _selectedFile.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
                tasks = ImportExcelTasks(stream);
            }
            else if (_tab == ImportTab.Mpp)
            {
                if (_selectedFile is null) return;
                using var stream = _selectedFile.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
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
        catch (GanttImportAuthException)
        {
            _errorMessage = Loc["TmGantt_ImportError"].Replace("{0}", "authentication failed");
            await OnImportError.InvokeAsync(_errorMessage);
        }
        catch (Exception ex)
        {
            _errorMessage = Loc["TmGantt_ImportError"].Replace("{0}", ex.Message);
            await OnImportError.InvokeAsync(_errorMessage);
        }
        finally
        {
            _isImporting = false;
        }
    }

    private IReadOnlyList<TmWorkItem> ImportExcelTasks(Stream stream)
    {
        var importerType = ResolveGanttExcelImporterType()
            ?? throw new InvalidOperationException(Loc["TmGantt_ImportXlsxPackageMissing"]);

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
