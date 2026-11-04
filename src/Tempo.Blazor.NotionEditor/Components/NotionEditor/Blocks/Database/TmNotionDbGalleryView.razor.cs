using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbGalleryView : TmComponentBase
{
    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField>  Fields          { get; set; } = [];
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseRecord> Records         { get; set; } = [];
    [Parameter]                 public bool                           ReadOnly        { get; set; }
    [Parameter]                 public GalleryCardSize                CardSize        { get; set; } = GalleryCardSize.Medium;
    [Parameter]                 public Guid?                          CoverFieldId    { get; set; }
    [Parameter]                 public CoverFit                       CoverFit        { get; set; } = CoverFit.Cover;
    [Parameter]                 public IReadOnlyList<Guid>?           PreviewFieldIds { get; set; }

    [Parameter] public EventCallback<IDatabaseRecord> OnRecordClicked { get; set; }
    [Parameter] public EventCallback                  OnNewRecord     { get; set; }

    // ── Computed state ───────────────────────────────────────────────────────

    private List<IDatabaseField> _previewFields = [];

    protected override void OnParametersSet() => ComputePreviewFields();

    private void ComputePreviewFields()
    {
        if (PreviewFieldIds is { Count: > 0 })
        {
            _previewFields = PreviewFieldIds
                .Select(id => Fields.FirstOrDefault(f => f.Id == id))
                .OfType<IDatabaseField>()
                .ToList();
        }
        else
        {
            _previewFields = Fields
                .Where(f => !f.IsPrimary && f.IsVisible)
                .Take(3)
                .ToList();
        }
    }

    // ── CSS helpers ──────────────────────────────────────────────────────────

    private string CardSizeClass => CardSize switch
    {
        GalleryCardSize.Small => "tm-dbg--small",
        GalleryCardSize.Large => "tm-dbg--large",
        _                     => "tm-dbg--medium"
    };

    private string CoverObjectFit => CoverFit == CoverFit.Contain ? "contain" : "cover";

    // ── Cover image ──────────────────────────────────────────────────────────

    private string? GetCoverUrl(IDatabaseRecord record)
    {
        if (CoverFieldId is not null)
        {
            var coverField = Fields.FirstOrDefault(f => f.Id == CoverFieldId);
            if (coverField is not null && record.Fields.TryGetValue(coverField.Id.ToString(), out var cv))
            {
                var url = ExtractFirstUrl(cv);
                if (!string.IsNullOrEmpty(url)) return url;
            }
        }

        var filesField = Fields.FirstOrDefault(f => f.Type == DatabaseFieldType.Files && f.IsVisible);
        if (filesField is not null && record.Fields.TryGetValue(filesField.Id.ToString(), out var fv))
        {
            var url = ExtractFirstUrl(fv);
            if (!string.IsNullOrEmpty(url)) return url;
        }

        return null;
    }

    private static string? ExtractFirstUrl(object? val) => val switch
    {
        string s when s.Length > 0           => s,
        string[] arr when arr.Length > 0     => arr[0],
        IEnumerable<string> list             => list.FirstOrDefault(),
        _                                    => null
    };

    // ── Card helpers ─────────────────────────────────────────────────────────

    private IDatabaseField? PrimaryField => Fields.FirstOrDefault(f => f.IsPrimary);

    private string GetPrimaryValue(IDatabaseRecord record)
    {
        var primary = PrimaryField;
        if (primary is null) return string.Empty;
        return record.Fields.TryGetValue(primary.Id.ToString(), out var v)
            ? NotionDatabaseValueFormatter.Format(v)
            : string.Empty;
    }

    private static string FormatFieldValue(IDatabaseRecord record, IDatabaseField field)
    {
        if (!record.Fields.TryGetValue(field.Id.ToString(), out var val) || val is null)
            return string.Empty;

        return NotionDatabaseValueFormatter.Format(val);
    }

    private async Task HandleCardClickAsync(IDatabaseRecord record)
        => await OnRecordClicked.InvokeAsync(record);

    private async Task HandleCardKeyAsync(KeyboardEventArgs e, IDatabaseRecord record)
    {
        if (e.Key is "Enter" or " ")
            await OnRecordClicked.InvokeAsync(record);
    }
}
