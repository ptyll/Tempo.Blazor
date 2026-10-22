using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// The editor canvas (Fáze 9A): the document rendered as the email it will become, with the selection /
/// drag-and-drop / keyboard contract the editor depends on. The block VISUALS are
/// <see cref="TmEmailCanvasBlock"/>'s job; the canvas owns the paper, the sections, the columns, the drop
/// zones and every <c>data-tm-*</c> selector a host or an E2E spec keys off.
/// </summary>
public partial class TmEmailTemplateCanvas
{
    /// <summary>Any HTML tag, removed to turn a block's content into a plain-text accessible name.</summary>
    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Tags();

    /// <summary>Any run of whitespace, collapsed in a brief content preview.</summary>
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace();

    /// <summary>The document being displayed.</summary>
    [Parameter, EditorRequired] public EmailTemplateDocument Document { get; set; } = new();

    /// <summary>The currently selected node id (block, column or section), if any.</summary>
    [Parameter] public Guid? SelectedId { get; set; }

    /// <summary>Raised when the selection changes.</summary>
    [Parameter] public EventCallback<Guid?> SelectedIdChanged { get; set; }

    /// <summary>Raised after the document is mutated by a block/section/column action.</summary>
    [Parameter] public EventCallback OnDocumentChanged { get; set; }

    /// <summary>Whether a drag is in progress (shows drop zones between blocks).</summary>
    [Parameter] public bool DragActive { get; set; }

    /// <summary>Raised when a block is dropped into a column at an index.</summary>
    [Parameter] public EventCallback<(Guid ColumnId, int Index)> OnDropInColumn { get; set; }

    /// <summary>Raised when an existing block begins being dragged.</summary>
    [Parameter] public EventCallback<Guid> OnBlockDragStart { get; set; }

    /// <summary>
    /// When true the canvas renders the document read-only: no inline block/section actions, no draggable
    /// blocks, no drop zones and no keyboard shortcut mutates the document (selection and navigation stay
    /// available so the inspector can still show what is selected).
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>
    /// Whether the canvas may request the document's remote images from the author's browser. Defaults to
    /// <see cref="Components.CanvasImageLoading.Load"/>; <see cref="CanvasImageLoading.Placeholder"/> paints
    /// a framed alt-text placeholder instead and issues no request at all (privacy / tracking protection).
    /// </summary>
    [Parameter] public CanvasImageLoading CanvasImageLoading { get; set; } = CanvasImageLoading.Load;

    private ElementReference _rootRef;
    private readonly Dictionary<Guid, ElementReference> _blockRefs = new();
    private readonly Dictionary<Guid, ElementReference> _sectionRefs = new();

    /// <summary>
    /// Moves keyboard focus into the canvas: to <paramref name="id"/> when that BLOCK or SECTION is still
    /// rendered, otherwise to the canvas root (a programmatic focus target, so focus never falls to
    /// <c>&lt;body&gt;</c> after a structural delete). Sections are looked up too, because "add section"
    /// selects and focuses a section — with only block references it fell through to the canvas root and
    /// the user had to find the new section by scrolling.
    /// </summary>
    /// <param name="id">The block or section to focus, or <see langword="null"/> for the canvas root.</param>
    public async ValueTask FocusAsync(Guid? id = null)
    {
        if (id is { } target)
        {
            if (_blockRefs.TryGetValue(target, out var block))
            {
                await block.FocusAsync();
                return;
            }

            if (_sectionRefs.TryGetValue(target, out var section))
            {
                await section.FocusAsync();
                return;
            }
        }

        await _rootRef.FocusAsync();
    }

    /// <summary>
    /// The "paper": the email is a fixed-width table in every mail client, so the canvas paints the width
    /// the document declares (<see cref="TemplateStyles.ContentWidth"/>) and never lets it overflow the
    /// editor panel. An unparseable width falls back to the 600 px MJML default — a crafted document must
    /// not steer the style attribute.
    /// <para>
    /// The document's <c>BackgroundColor</c> and <c>FontFamily</c> are painted through
    /// <see cref="EmailCanvasValue"/> too (review 19): they were silently ignored, so a template whose body is
    /// cream with a serif face looked like the default white sans on the canvas while the export and the
    /// preview showed something else. An invalid colour falls back to <c>--tm-paper</c> and an invalid stack to
    /// the canvas font, never to the raw value.
    /// </para>
    /// </summary>
    private string PaperStyle => EmailCanvasValue.Style(
        ("inline-size", EmailCanvasValue.Length(Document.Styles.ContentWidth) ?? "600px"),
        ("max-inline-size", "100%"),
        ("background-color", EmailCanvasValue.Color(Document.Styles.BackgroundColor) ?? "var(--tm-paper)"),
        ("font-family", EmailCanvasValue.FontFamily(Document.Styles.FontFamily)));

    /// <summary>
    /// The section box: background, border, radius, padding and text alignment, i.e. the properties MJML's
    /// <c>mj-section</c> actually paints. Background POSITION/SIZE/REPEAT are deliberately not approximated —
    /// the canvas paints a background image as an image, so it never builds a CSS <c>url(…)</c>.
    /// </summary>
    private static string SectionStyle(EmailSection section) => EmailCanvasValue.Style(
        ("background-color", EmailCanvasValue.Color(section.BackgroundColor)),
        ("border", EmailCanvasValue.Border(section.Border)),
        ("border-radius", EmailCanvasValue.Length(section.BorderRadius)),
        ("text-align", EmailCanvasValue.Align(section.TextAlign)),
        ("padding", EmailCanvasValue.Padding(section.Padding)));

    /// <summary>
    /// The section background image, or <see langword="null"/> when it may not be requested. It goes through
    /// <see cref="EmailCanvasValue.ImageUrl"/> (not <see cref="EmailCanvasValue.Url"/>), because a background is
    /// a REQUEST: only an absolute <c>http</c>/<c>https</c> URL may become one.
    /// </summary>
    private string? SectionBackground(EmailSection section)
        => CanvasImageLoading == CanvasImageLoading.Load
            ? EmailCanvasValue.ImageUrl(section.BackgroundUrl)
            : null;

    /// <summary>
    /// Whether the section declares a background that the canvas refused, so the stage can say WHY there is
    /// no picture (review finding 8: a silently missing background looks like a broken editor).
    /// </summary>
    private bool IsSectionBackgroundBlocked(EmailSection section)
        => CanvasImageLoading == CanvasImageLoading.Load
           && !string.IsNullOrWhiteSpace(section.BackgroundUrl)
           && EmailUrlPolicy.FindBlockedScheme(section.BackgroundUrl) is not null;

    /// <summary>The block kind as a test hook on the wrapper (<c>text</c>, <c>button</c>, …).</summary>
    private static string BlockKind(EmailBlockBase block) => block.Type.ToString().ToLowerInvariant();

    /// <summary>The block kind as the last segment of its localization key (<c>block.text.name</c>).</summary>
    private static string BlockKey(EmailBlockBase block) => BlockKind(block);

    private bool IsSelected(Guid id) => SelectedId == id;

    private string SelectedClass(Guid id) => IsSelected(id) ? "is-selected" : string.Empty;

    /// <summary>
    /// The wrapper's class list. <c>is-first-in-column</c> tells the stylesheet that this block has no room
    /// ABOVE it for the action bar (the section's own bar is there), so the bar stays inside the block.
    /// </summary>
    private string BlockClass(EmailBlockBase block, int index)
        => "tm-email-canvas-block " + SelectedClass(block.Id) + (index == 0 ? " is-first-in-column" : string.Empty);

    /// <summary>
    /// <c>aria-current="true"</c> on the selected item, and NOTHING when it is not selected (review finding
    /// 20). The items are a <c>role="list"</c>/<c>listitem</c> structure, where <c>aria-selected</c> is not a
    /// supported state — assistive technology ignored it, so the selection was invisible to a screen reader.
    /// </summary>
    private string? AriaCurrent(Guid id) => IsSelected(id) ? "true" : null;

    /// <summary>
    /// The roving tab stop of the list (review finding 20): the SELECTED item is reachable with one Tab, and
    /// with nothing selected the first block is. Every other item is <c>-1</c>, so a 52-block document does
    /// not put 52 tab stops between the toolbox and the inspector. Arrow keys move the selection AND the
    /// focus (see <see cref="FocusAfterRender"/>).
    /// </summary>
    private string RovingTabIndex(Guid id, Guid? tabStopId)
        => IsSelected(id) || (SelectedId is null && id == tabStopId) ? "0" : "-1";

    /// <summary>The item that owns the single tab stop while nothing is selected.</summary>
    private Guid? FirstTabStopId => DocumentTree.AllBlocks(Document).FirstOrDefault()?.Id;

    /// <summary>The 1-based number used in a section's accessible name ("Section 2").</summary>
    private string SectionNumber(EmailSection section)
    {
        for (var i = 0; i < Document.Sections.Count; i++)
        {
            if (Document.Sections[i].Id == section.Id)
            {
                return (i + 1).ToString(CultureInfo.InvariantCulture);
            }
        }

        return "1";
    }

    /// <summary>
    /// The accessible name of a block (review finding 13): the localized TYPE plus its brief content, so a
    /// screen reader announces "Text: Welcome, {{ first_name }}" instead of five identical "Text" entries.
    /// The content is stripped of tags (the label is text), a template token stays as the author wrote it, and
    /// it is capped so a long paragraph cannot become the whole label.
    /// </summary>
    private string BlockLabel(EmailBlockBase block)
    {
        var type = Loc[$"block.{BlockKey(block)}.name"];
        var content = BriefContent(block);
        return content.Length == 0
            ? Loc["EmailEditor_Canvas_BlockLabelEmpty", type]
            : Loc["EmailEditor_Canvas_BlockLabel", type, content];
    }

    /// <summary>The longest brief content an accessible name carries.</summary>
    internal const int BlockLabelContentLength = 60;

    /// <summary>The first non-empty content of a block, as plain text (tags removed, whitespace collapsed).</summary>
    private static string BriefContent(EmailBlockBase block)
    {
        var content = block switch
        {
            EmailTextBlock text => StripTags(text.Content),
            EmailButtonBlock button => button.Text,
            EmailImageBlock image => image.Alt,
            EmailRawBlock raw => StripTags(raw.Content),
            EmailTableBlock table => FirstNonEmpty(
                table.Rows.SelectMany(row => row.Cells).Select(cell => cell.Text)),
            EmailSocialBlock social => FirstNonEmpty(
                social.Elements.Select(element => element.Label ?? element.Name)),
            EmailNavbarBlock navbar => Join(navbar.Links.Select(link => link.Text)),
            EmailCarouselBlock carousel => FirstNonEmpty(
                carousel.Images.Select(image => image.Alt ?? image.Title)),
            EmailAccordionBlock accordion => Join(accordion.Items.Select(item => item.Title)),
            EmailHeroBlock hero => FirstNonEmpty(hero.Blocks.Select(BriefContent)),
            EmailWrapperBlock wrapper => FirstNonEmpty(
                wrapper.Sections.SelectMany(section => section.Columns)
                    .SelectMany(column => column.Blocks).Select(BriefContent)),
            EmailGroupBlock group => FirstNonEmpty(
                group.Columns.SelectMany(column => column.Blocks).Select(BriefContent)),
            _ => null,
        };

        var collapsed = content is null ? string.Empty : Whitespace().Replace(content, " ").Trim();
        return collapsed.Length > BlockLabelContentLength
            ? collapsed[..BlockLabelContentLength].TrimEnd() + "…"
            : collapsed;
    }

    private static string? FirstNonEmpty(IEnumerable<string?> values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? Join(IEnumerable<string?> values)
    {
        var joined = string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        return joined.Length == 0 ? null : joined;
    }

    /// <summary>A text run without its tags (the accessible name is text, not markup).</summary>
    private static string? StripTags(string? content)
        => string.IsNullOrEmpty(content) ? null : System.Net.WebUtility.HtmlDecode(Tags().Replace(content, string.Empty));

    private Task SelectAsync(Guid id) => SelectedIdChanged.InvokeAsync(id);

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        if (ReadOnly && e.Key is "Delete" or "Backspace")
        {
            return;
        }

        if (e.Key == "Escape")
        {
            await SelectedIdChanged.InvokeAsync(null);
            return;
        }

        if (SelectedId is not Guid selected)
        {
            if (e.Key is "ArrowDown" or "ArrowUp")
            {
                var first = DocumentTree.AllBlocks(Document).FirstOrDefault();
                if (first is not null) await SelectedIdChanged.InvokeAsync(first.Id);
            }
            return;
        }

        switch (e.Key)
        {
            case "Delete" or "Backspace":
                await DeleteSelectionAsync(selected);
                break;
            case "ArrowDown":
                await NavigateAsync(selected, 1);
                break;
            case "ArrowUp":
                await NavigateAsync(selected, -1);
                break;
            case "d" or "D" when e.CtrlKey && !ReadOnly:
                await DuplicateSelectionAsync(selected);
                break;
        }
    }

    private async Task DeleteSelectionAsync(Guid id)
    {
        var block = Document.FindBlock(id);
        if (block is not null) { await DeleteBlockAsync(block); return; }
        var section = Document.Sections.FirstOrDefault(s => s.Id == id);
        if (section is not null) await DeleteSectionAsync(section);
    }

    private async Task DuplicateSelectionAsync(Guid id)
    {
        var block = Document.FindBlock(id);
        if (block is not null) { await DuplicateBlockAsync(block); return; }
        var section = Document.Sections.FirstOrDefault(s => s.Id == id);
        if (section is not null) await DuplicateSectionAsync(section);
    }

    private async Task NavigateAsync(Guid id, int direction)
    {
        var blocks = DocumentTree.AllBlocks(Document).ToList();
        var index = blocks.FindIndex(b => b.Id == id);
        if (index < 0)
        {
            if (blocks.Count > 0) await MoveSelectionAsync(blocks[0].Id);
            return;
        }
        var target = index + direction;
        if (target < 0 || target >= blocks.Count) return;
        await MoveSelectionAsync(blocks[target].Id);
    }

    /// <summary>
    /// Selects <paramref name="id"/> and remembers that the keyboard focus must follow it once the parent has
    /// re-rendered with the new selection (review finding 20): before this, an Arrow key moved the indigo
    /// frame while the focus stayed on the previous block, so the next Arrow went to the wrong neighbour.
    /// </summary>
    private async Task MoveSelectionAsync(Guid id)
    {
        _pendingFocusId = id;
        await SelectedIdChanged.InvokeAsync(id);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocusId is not { } target)
        {
            return;
        }

        _pendingFocusId = null;
        try
        {
            await FocusAsync(target);
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { }
    }

    /// <summary>Where the keyboard focus has to land after the next render (an Arrow key moved the selection).</summary>
    private Guid? _pendingFocusId;

    // ── Block actions ───────────────────────────────────────────────────────────────────────

    private async Task MoveBlockAsync(EmailBlockBase block, int direction)
    {
        if (ReadOnly) return;
        var column = Document.FindParentColumn(block.Id);
        if (column is null) return;
        var index = IndexOf(column.Blocks, b => b.Id == block.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= column.Blocks.Count) return;
        (column.Blocks[index], column.Blocks[target]) = (column.Blocks[target], column.Blocks[index]);
        await OnDocumentChanged.InvokeAsync();
    }

    private async Task DuplicateBlockAsync(EmailBlockBase block)
    {
        if (ReadOnly) return;
        var copy = Document.DuplicateBlock(block.Id);
        if (copy is not null) await SelectedIdChanged.InvokeAsync(copy.Id);
        await OnDocumentChanged.InvokeAsync();
    }

    private async Task DeleteBlockAsync(EmailBlockBase block)
    {
        if (ReadOnly) return;
        Document.RemoveBlock(block.Id);
        if (SelectedId == block.Id) await SelectedIdChanged.InvokeAsync(null);
        await OnDocumentChanged.InvokeAsync();
    }

    // ── Section / column actions ────────────────────────────────────────────────────────────

    private async Task AddColumnAsync(EmailSection section)
    {
        if (ReadOnly) return;
        var column = new EmailColumn();
        section.AddColumn(column);
        await SelectedIdChanged.InvokeAsync(column.Id);
        await OnDocumentChanged.InvokeAsync();
    }

    private async Task RemoveColumnAsync(EmailSection section, EmailColumn column)
    {
        if (ReadOnly) return;
        section.RemoveColumn(column.Id);
        if (SelectedId == column.Id) await SelectedIdChanged.InvokeAsync(null);
        await OnDocumentChanged.InvokeAsync();
    }

    private async Task DuplicateSectionAsync(EmailSection section)
    {
        if (ReadOnly) return;
        var copy = Document.DuplicateSection(section.Id);
        if (copy is not null) await SelectedIdChanged.InvokeAsync(copy.Id);
        await OnDocumentChanged.InvokeAsync();
    }

    private async Task DeleteSectionAsync(EmailSection section)
    {
        if (ReadOnly) return;
        Document.RemoveSection(section.Id);
        if (SelectedId == section.Id) await SelectedIdChanged.InvokeAsync(null);
        await OnDocumentChanged.InvokeAsync();
    }

    private async Task MoveSectionAsync(EmailSection section, int direction)
    {
        if (ReadOnly) return;
        var index = IndexOf(Document.Sections, s => s.Id == section.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= Document.Sections.Count) return;
        Document.MoveSection(section.Id, target);
        await OnDocumentChanged.InvokeAsync();
    }

    private static int IndexOf<T>(IList<T> items, Predicate<T> predicate)
    {
        for (var i = 0; i < items.Count; i++)
            if (predicate(items[i]))
                return i;

        return -1;
    }
}
