using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.DocumentEditor;

// Redesign 146c7e1b, phase 7 (DE-1.8 + DE-2.6): the doc region has two MODES besides editing - the print preview (no ribbon, page thumbnails on
// the left, the page / zoom bar above the page, the actions and the document information on the right) and the comparison (the diff as
// text, aligned by block, with "Back to document"). The canvas engine stays mounted in both: the print preview is the engine's own
// openPrintPreview / closePrintPreview state, the comparison simply hides the page surface while it is shown.
public partial class TmDocumentEditor
{
    private const int PrintPreviewZoomMin = 50;
    private const int PrintPreviewZoomMax = 200;
    private const int PrintPreviewZoomStep = 10;
    private static readonly int[] PrintPreviewZoomPresets = [50, 75, 100, 125, 150, 200];

    private readonly string _printActionsTitleId = $"tm-document-print-actions-{Guid.NewGuid():N}";
    private readonly string _printInfoTitleId = $"tm-document-print-info-{Guid.NewGuid():N}";

    private bool _fitPrintPreviewOnRender;
    private bool _restoreZoomOnRender;
    // The zoom the reader was editing at, kept while the print preview fits the page; restored when the preview closes.
    private int? _zoomBeforePrintPreview;

    // Commands that change the view, not the document: they run while the comparison is shown. Everything else leaves the comparison first
    // (the diff is text, the editing commands act on the canvas behind it), then runs.
    private static readonly HashSet<string> ViewOnlyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "setZoom", "fitWidth", "fitPage", "zoomIn", "zoomOut", "zoom", "zoomPageWidth",
        "openPrintPreview", "closePrintPreview", "printDocument", "fullscreen",
        "toggleRuler", "showRuler", "toggleNonPrintingCharacters", "showBlocks",
        "printLayout", "webLayout", "readingView", "outlineView", "commandPalette", "save", "openVersions", "openComments", "openRevisions",
    };

    private async Task LeaveComparisonForCommandAsync(string command)
    {
        if (DiffModeActive && !ViewOnlyCommands.Contains(command))
        {
            await CloseDiffModeAsync();
        }
    }

    // A registry command knows whether it affects the data; a command without a registry state falls back to the view-only list.
    private async Task LeaveComparisonForRegistryCommandAsync(string command)
    {
        if (DiffModeActive && (_commandRegistry.GetState(command)?.AffectsData ?? !ViewOnlyCommands.Contains(command)))
        {
            await CloseDiffModeAsync();
        }
    }

    private async Task RestoreEditingZoomAsync()
    {
        if (_zoomBeforePrintPreview is int zoom)
        {
            _zoomBeforePrintPreview = null;
            await SetZoomPercentAsync(zoom);
        }
    }
    private DocumentCompareOpenRequest? _diffRequest;
    private string? _diffOldMeta;
    private string? _diffNewMeta;
    private ITmRelativeTimeFormatter? _modesRelativeTime;

    [Inject] private IServiceProvider ModesServices { get; set; } = default!;

    private bool IsMobileLayout => _layout == TmLayoutMode.Mobile;

    // The comparison is shown only while the document is edited normally; the print preview owns the doc region while it is up.
    private bool DiffModeActive => _diffRequest is not null && !CanvasPrintPreviewActive;

    private int PrintPreviewPageCount => Math.Max(1, DocumentPageCount);

    private int PrintPreviewCurrentPage => Math.Clamp(_activePageIndex + 1, 1, PrintPreviewPageCount);

    private IEnumerable<int> PrintPreviewZoomOptions
        => PrintPreviewZoomPresets.Contains(_zoomPercent) ? PrintPreviewZoomPresets : PrintPreviewZoomPresets.Append(_zoomPercent).Order();

    private string PrintPreviewZoomValue => _zoomPageWidth ? "fit" : _zoomPercent.ToString(CultureInfo.InvariantCulture);

    // "A4 (210 x 297 mm)": the page size is stored in points, the paper is read in millimetres.
    private string PrintPreviewPaperText
    {
        get
        {
            var size = ActivePageSettings.Size;
            var width = (int)Math.Round(size.Width * 25.4 / 72d, MidpointRounding.AwayFromZero);
            var height = (int)Math.Round(size.Height * 25.4 / 72d, MidpointRounding.AwayFromZero);
            return string.IsNullOrWhiteSpace(size.Name)
                ? Loc["TmDocumentEditor_PrintPreviewPaperCustom", width, height]
                : Loc["TmDocumentEditor_PrintPreviewPaperNamed", size.Name!, width, height];
        }
    }

    private string PrintPreviewOrientationText => ActivePageSettings.Landscape
        ? Loc["TmDocumentEditor_OrientationLandscape"]
        : Loc["TmDocumentEditor_OrientationPortrait"];

    private Task PrintPreviewStepPageAsync(int delta)
        => NavigateToPageAsync(Math.Clamp(_activePageIndex + delta, 0, PrintPreviewPageCount - 1));

    // A new key makes the browser input take the clamped value back, also when it equals the one the component rendered before.
    private int _printPageInputEpoch;

    private Task PrintPreviewPageInputAsync(ChangeEventArgs args)
    {
        _printPageInputEpoch++;
        return int.TryParse(args.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var page)
            ? NavigateToPageAsync(Math.Clamp(page, 1, PrintPreviewPageCount) - 1)
            : Task.CompletedTask;
    }

    private Task PrintPreviewStepZoomAsync(int delta)
        => SetZoomPercentAsync(Math.Clamp(_zoomPercent + delta, PrintPreviewZoomMin, PrintPreviewZoomMax));

    private Task PrintPreviewZoomSelectAsync(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        if (string.Equals(value, "fit", StringComparison.Ordinal))
        {
            return SetZoomPageWidthAsync();
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
            ? SetZoomPercentAsync(percent)
            : Task.CompletedTask;
    }

    /// <summary>"Show differences" of the version panel: the two picked snapshots are compared locally and shown as the comparison mode.</summary>
    private async Task ShowVersionDiffAsync(DocumentVersionDiffRequest request)
    {
        var baseDocument = ReadVersionSnapshot(request.Base);
        var compareDocument = ReadVersionSnapshot(request.Compare);
        if (baseDocument is null || compareDocument is null)
        {
            _versionMessage = Loc["TmDocumentEditor_CompareError"];
            await InvokeAsync(StateHasChanged);
            return;
        }

        var result = new DocumentComparisonService().Compare(baseDocument, compareDocument);
        _diffOldMeta = VersionMeta(request.Base);
        _diffNewMeta = VersionMeta(request.Compare);
        _diffRequest = new DocumentCompareOpenRequest(
            result,
            null,
            DocumentVersionText.Title(Loc, request.Base),
            DocumentVersionText.Title(Loc, request.Compare));
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>"Open comparison" of the compare dialog: the dialog closes and the finished comparison becomes the comparison mode.</summary>
    private async Task OpenComparisonModeAsync(DocumentCompareOpenRequest request)
    {
        CloseCompareDialog();
        _diffOldMeta = null;
        _diffNewMeta = null;
        _diffRequest = request;
        await InvokeAsync(StateHasChanged);
    }

    private async Task CloseDiffModeAsync()
    {
        if (_diffRequest is null)
        {
            return;
        }

        _diffRequest = null;
        _diffOldMeta = null;
        _diffNewMeta = null;
        await InvokeAsync(StateHasChanged);
        await FocusDocumentAsync();
    }

    // A snapshot that is not valid JSON is a failed comparison ("Document comparison failed."), never an unhandled exception.
    private static DocumentEditorDocument? ReadVersionSnapshot(DocumentVersion version)
    {
        if (string.IsNullOrWhiteSpace(version.Snapshot.Json))
        {
            return null;
        }

        try
        {
            return DocumentEditorJson.Deserialize(version.Snapshot.Json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // "Minor version - 3 May 2024 09:18 - Eva Novakova": the kind, the absolute moment and the author of a compared version.
    private string VersionMeta(DocumentVersion version)
    {
        var formatter = _modesRelativeTime ??= TmRelativeTime.Resolve(ModesServices, Loc);
        var author = string.IsNullOrWhiteSpace(version.Author.DisplayName)
            ? Loc["TmDocumentEditor_UnknownAuthor"].ToString()
            : version.Author.DisplayName;
        return Loc["TmDocumentEditor_VersionMeta", DocumentVersionText.KindLabel(Loc, version.Kind), formatter.Absolute(version.CreatedAt), author];
    }
}
