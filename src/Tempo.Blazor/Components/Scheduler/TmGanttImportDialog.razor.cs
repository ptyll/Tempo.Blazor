using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;
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
    private IJSObjectReference? _filePickerModule;

    /// <summary>Whether the dialog is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Fires when import completes successfully.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TmWorkItem>> OnImportCompleted { get; set; }

    /// <summary>Fires when import fails with an error message.</summary>
    [Parameter] public EventCallback<string> OnImportError { get; set; }

    /// <summary>Fires when the dialog should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    private void OnFileChangedAsync(InputFileChangeEventArgs e)
    {
        _selectedFile = e.File;
        _selectedFileName = e.File.Name;
        _errorMessage = null;
    }

    /// <summary>
    /// Opens the native file chooser by clicking the visually hidden <see cref="InputFile"/> inside
    /// the button's user gesture — so Enter, Space and pointer activation all reach the chooser.
    /// The module is imported lazily on first use: the dialog renders before JS interop is
    /// available on prerender, and importing here keeps the chooser inside the same activation.
    /// </summary>
    private async Task OpenFilePickerAsync()
    {
        _filePickerModule ??= await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
        await _filePickerModule.InvokeVoidAsync("openFilePicker", _fileInputWrap);
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
