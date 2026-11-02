using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbImportExport : ComponentBase
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Suffixes the file input id and the export radio name so two dialogs do not collide.</summary>
    private readonly string _idSuffix = Guid.NewGuid().ToString("N");

    private string FileInputId => $"tm-dbie-file-{_idSuffix}";

    private string ExportViewName => $"tm-dbie-view-{_idSuffix}";

    [CascadingParameter] private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public Guid                           DatabaseId    { get; set; }
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField>  Fields        { get; set; } = [];
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseView>   Views         { get; set; } = [];
    [Parameter]                 public string                         DatabaseName  { get; set; } = string.Empty;
    [Parameter]                 public bool                           ReadOnly      { get; set; }
    [Parameter]                 public EventCallback                  OnImported    { get; set; }
    [Parameter]                 public EventCallback                  OnClose       { get; set; }

    // ── Tab / step enums ──────────────────────────────────────────────────────

    private enum ActiveTab  { Import, Export }
    private enum ImportStep { Upload, MapColumns }

    private ActiveTab  _tab        = ActiveTab.Import;
    private ImportStep _importStep = ImportStep.Upload;

    // ── Import state ──────────────────────────────────────────────────────────

    private string   _selectedFileName = string.Empty;
    private byte[]?  _fileBytes;
    private string[] _csvHeaders  = [];
    private string[] _csvPreview  = [];
    private int      _csvDataRows;
    private Dictionary<int, Guid?> _columnMapping = new();
    private bool    _importLoading;
    private string? _importError;

    private const long MaxFileBytes = 10L * 1024 * 1024;

    // ── Export state ──────────────────────────────────────────────────────────

    private Guid?   _exportViewId;
    private bool    _exportLoading;
    private string? _exportError;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        if (_tab == ActiveTab.Import && ReadOnly)
            _tab = ActiveTab.Export;

        if (_exportViewId is null && Views.Count > 0)
            _exportViewId = Views[0].Id;
    }

    // ── Tab ───────────────────────────────────────────────────────────────────

    private void SetTab(ActiveTab tab)
    {
        if (tab == ActiveTab.Import && ReadOnly) return;
        _tab = tab;
    }

    // ── Import ────────────────────────────────────────────────────────────────

    private async Task HandleFileSelectedAsync(InputFileChangeEventArgs e)
    {
        _importError = null;
        var file     = e.File;

        if (!file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            _importError = Loc["TmNotionDbImportExport_FileMustBeCsv"];
            return;
        }

        if (file.Size > MaxFileBytes)
        {
            _importError = Loc["TmNotionDbImportExport_FileTooBig"];
            return;
        }

        _importLoading = true;
        StateHasChanged();

        try
        {
            await using var stream = file.OpenReadStream(MaxFileBytes);
            using var ms           = new MemoryStream();
            await stream.CopyToAsync(ms);

            _fileBytes        = ms.ToArray();
            _selectedFileName = file.Name;

            var (headers, preview, dataRows) = ParseCsvPreview(_fileBytes);
            _csvHeaders  = headers;
            _csvPreview  = preview;
            _csvDataRows = dataRows;

            AutoMapColumns();
            _importStep = ImportStep.MapColumns;
        }
        catch
        {
            _importError = Loc["TmNotionDbImportExport_FileMustBeCsv"];
        }
        finally
        {
            _importLoading = false;
        }
    }

    private void AutoMapColumns()
    {
        _columnMapping = new Dictionary<int, Guid?>();
        for (var i = 0; i < _csvHeaders.Length; i++)
        {
            var match = Fields.FirstOrDefault(f =>
                string.Equals(f.Name, _csvHeaders[i], StringComparison.OrdinalIgnoreCase));
            _columnMapping[i] = match?.Id;
        }
    }

    private void BackToUpload()
    {
        _importStep       = ImportStep.Upload;
        _selectedFileName = string.Empty;
        _fileBytes        = null;
        _csvHeaders       = [];
        _csvPreview       = [];
        _csvDataRows      = 0;
        _importError      = null;
    }

    private async Task HandleImportAsync()
    {
        if (_fileBytes is null || Context.DatabaseProvider is null) return;

        _importLoading = true;
        _importError   = null;
        StateHasChanged();

        try
        {
            using var outStream = BuildMappedCsvStream();
            outStream.Position  = 0;
            await Context.DatabaseProvider.ImportCsvAsync(DatabaseId.ToString(), outStream);
            await OnImported.InvokeAsync();
            await OnClose.InvokeAsync();
        }
        catch
        {
            _importError = Loc["TmNotionDbImportExport_ImportError"];
        }
        finally
        {
            _importLoading = false;
        }
    }

    private MemoryStream BuildMappedCsvStream()
    {
        var output = new MemoryStream();
        var writer = new StreamWriter(output, leaveOpen: true);

        var mappedCols = _columnMapping
            .Where(kv => kv.Value.HasValue)
            .OrderBy(kv => kv.Key)
            .ToList();

        writer.WriteLine(string.Join(",", mappedCols.Select(kv =>
            EscapeCsvField(Fields.FirstOrDefault(f => f.Id == kv.Value)?.Name ?? string.Empty))));

        using var reader = new StreamReader(new MemoryStream(_fileBytes!));
        reader.ReadLine();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols  = ParseCsvLine(line);
            var parts = mappedCols.Select(kv =>
                EscapeCsvField(kv.Key < cols.Length ? cols[kv.Key] : string.Empty));
            writer.WriteLine(string.Join(",", parts));
        }

        writer.Flush();
        return output;
    }

    private static string EscapeCsvField(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return '"' + s.Replace("\"", "\"\"") + '"';
        return s;
    }

    // ── Export ────────────────────────────────────────────────────────────────

    private async Task HandleExportAsync()
    {
        if (Context.DatabaseProvider is null) return;

        _exportLoading = true;
        _exportError   = null;
        StateHasChanged();

        try
        {
            var stream = await Context.DatabaseProvider.ExportCsvAsync(
                DatabaseId.ToString(),
                _exportViewId?.ToString());

            var safeName = string.IsNullOrWhiteSpace(DatabaseName)
                ? "export"
                : new string(DatabaseName
                    .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_')
                    .ToArray());

            using var streamRef = new DotNetStreamReference(stream);
            await JS.InvokeVoidAsync("tmDb.downloadFileFromStream", $"{safeName}.csv", streamRef);
        }
        catch
        {
            _exportError = Loc["TmNotionDbImportExport_ExportError"];
        }
        finally
        {
            _exportLoading = false;
            StateHasChanged();
        }
    }

    // ── CSV helpers ───────────────────────────────────────────────────────────

    private static (string[] headers, string[] preview, int dataRows) ParseCsvPreview(byte[] bytes)
    {
        using var ms     = new MemoryStream(bytes);
        using var reader = new StreamReader(ms);

        var headers  = ParseCsvLine(reader.ReadLine() ?? string.Empty);
        var preview  = Array.Empty<string>();
        var dataRows = 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (dataRows == 0) preview = ParseCsvLine(line);
            dataRows++;
        }

        return (headers, preview, dataRows);
    }

    private static string[] ParseCsvLine(string line)
    {
        var result   = new List<string>();
        var field    = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                { field.Append('"'); i++; }
                else
                { inQuotes = !inQuotes; }
            }
            else if (c == ',' && !inQuotes)
            { result.Add(field.ToString()); field.Clear(); }
            else
            { field.Append(c); }
        }

        result.Add(field.ToString());
        return result.ToArray();
    }

    private async Task CloseAsync() => await OnClose.InvokeAsync();
}
