using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Properties panel for the wireframe editor.
/// Shows editable fields for the currently selected element(s).
///
/// <para>Single-select: all <see cref="PropDef"/> fields grouped by category, plus
/// always-visible Layout (X/Y/W/H) and Element (Type, ZIndex) sections.</para>
///
/// <para>Multi-select: common props across all selected element types, with "Mixed"
/// indicator when values differ. Changes apply to all selected elements.</para>
///
/// <para>All mutations are routed through the cascaded <see cref="WireframeCommandStack"/>
/// when available.</para>
/// </summary>
public partial class TmWireframePropertiesPanel : TmComponentBase, IDisposable
{
    // ── DI ────────────────────────────────────────────────────────────────────

    [Inject] private WireframeComponentRegistry _registry { get; set; } = default!;

    // ── Cascaded command stack ────────────────────────────────────────────────

    /// <summary>Command stack cascaded from the parent <see cref="TmWireframeEditor"/>. When present, all mutations are recorded for undo/redo.</summary>
    [CascadingParameter] public WireframeCommandStack? CommandStack { get; set; }

    // ── Parameters ────────────────────────────────────────────────────────────

    /// <summary>Document being edited.</summary>
    [Parameter] public WireframeDocument? Document { get; set; }

    /// <summary>Raised after every property change.</summary>
    [Parameter] public EventCallback<WireframeDocument> DocumentChanged { get; set; }

    /// <summary>Currently selected element ids.</summary>
    [Parameter] public string[] SelectedIds { get; set; } = [];

    /// <summary>Currently selected connector ids.</summary>
    [Parameter] public string[] SelectedConnectorIds { get; set; } = [];

    /// <summary>Raised when the user requests copy style from the selected element.</summary>
    [Parameter] public EventCallback OnCopyStyle { get; set; }

    /// <summary>Raised when the user requests paste style to the selected elements.</summary>
    [Parameter] public EventCallback OnPasteStyle { get; set; }

    /// <summary>Raised when the user requests paste size to the selected elements.</summary>
    [Parameter] public EventCallback OnPasteSize { get; set; }

    /// <summary>Prevent all editing interactions.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Additional CSS class on the panel wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Optional application scope used to resolve custom wireframe components.</summary>
    [Parameter] public WireframeComponentScope? ComponentScope { get; set; }

    /// <summary>
    /// Whether the canvas grid is shown (the "Show grid" toggle of the Grid section shown on an empty selection).
    /// The panel never assigns this parameter: it keeps a local copy, raises <see cref="ShowGridChanged"/> and
    /// re-adopts the host value whenever the parameter changes. Defaults to <c>true</c>.
    /// </summary>
    [Parameter] public bool ShowGrid { get; set; } = true;

    /// <summary>Raised with the new value when the user toggles "Show grid".</summary>
    [Parameter] public EventCallback<bool> ShowGridChanged { get; set; }

    /// <summary>
    /// Grid cell size in pixels (0 = grid snapping off). The panel never assigns this parameter: it keeps a local
    /// copy, raises <see cref="GridSizeChanged"/> and re-adopts the host value whenever the parameter changes.
    /// Defaults to 8; the input accepts 0–128.
    /// </summary>
    [Parameter] public int GridSize { get; set; } = 8;

    /// <summary>Raised with the new (clamped 0–128) grid size when the user edits "Grid size".</summary>
    [Parameter] public EventCallback<int> GridSizeChanged { get; set; }

    /// <summary>
    /// Raised with the new (width, height) AFTER a <see cref="ResizeCanvasCommand"/> was pushed for the active page
    /// (device preset or "Apply"), so the host can update its canvas runtime.
    /// </summary>
    [Parameter] public EventCallback<(double Width, double Height)> OnCanvasResized { get; set; }

    /// <summary>
    /// Raised when the user presses "Delete element". When it has a delegate the host performs the deletion (the
    /// panel does nothing else); otherwise the panel removes the selected elements itself through
    /// <see cref="RemoveElementsCommand"/> (undoable when a <see cref="WireframeCommandStack"/> is cascaded).
    /// </summary>
    [Parameter] public EventCallback OnDelete { get; set; }

    /// <summary>Whether the wireframe clipboard currently holds a copied style.</summary>
    public bool HasClipboardStyle => WireframeClipboard.HasStyle;

    // ── Derived state (recomputed in OnParametersSet) ─────────────────────────

    private List<WireframeElement>     _elements      = [];
    private List<WireframeConnector>   _connectors    = [];
    private WireframeComponentDef?     _def;           // null in multi-type selection
    private List<PropDef>              _commonProps    = [];
    private List<string>               _propCategories = [];
    private bool                       _isMulti;
    private bool                       _sameType;

    // Layout display values (MixedMarker when multi and values differ)
    private string? _layoutX, _layoutY, _layoutW, _layoutH;

    // Validation state: propName → error message
    private readonly Dictionary<string, string> _validationErrors = [];

    // Per-field debounce tokens — each field gets its own CTS so concurrent edits don't cancel each other
    private readonly Dictionary<string, CancellationTokenSource> _debounceCts = new();
    private const int DebounceMs = 350;

    internal const string MixedMarker = "__mixed__";

    // Canvas / page / grid sections (empty selection). Local state: the panel never assigns its [Parameter]s.
    private readonly string _idPrefix = "wd-props-" + Guid.NewGuid().ToString("N")[..8];
    private string _presetId => _idPrefix + "-preset";
    private string _canvasWId => _idPrefix + "-cw";
    private string _canvasHId => _idPrefix + "-ch";
    private string _pageNameId => _idPrefix + "-page-name";
    private string _gridSizeId => _idPrefix + "-grid-size";
    private string _elementNameId => _idPrefix + "-el-name";

    /// <summary>
    /// A stable, panel-unique DOM id for a control (<c>&lt;label for&gt;</c> / <c>aria-labelledby</c> target). Several
    /// panels can live on one page (editor + standalone), so the id carries the per-instance prefix; the key is reduced
    /// to id-safe characters because it can embed a component property name.
    /// </summary>
    private string FieldId(string key)
        => _idPrefix + "-" + new string(key.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());

    private string _canvasWInput = "";
    private string _canvasHInput = "";
    private bool _canvasSizeInvalid;
    private string _canvasErrorId => _idPrefix + "-canvas-error";

    /// <summary>Smallest accepted canvas edge in SVG units (the inputs' <c>min</c>).</summary>
    internal const double MinCanvasSize = 100;

    /// <summary>Largest accepted canvas edge in SVG units (the inputs' <c>max</c>).</summary>
    internal const double MaxCanvasSize = 9999;

    private void OnCanvasSizeInput(ChangeEventArgs e, bool isWidth)
    {
        var text = e.Value?.ToString() ?? string.Empty;
        if (isWidth) _canvasWInput = text; else _canvasHInput = text;
        _canvasSizeInvalid = false;   // typing again withdraws the message until the next Apply
    }
    private string? _seededPageId;
    private double _seededW = double.NaN, _seededH = double.NaN;
    private bool _customPresetChosen;

    private bool _showGrid = true;
    private int _gridSize = 8;
    private bool? _lastShowGridParam;
    private int? _lastGridSizeParam;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        AdoptHostGrid();
        SeedCanvasInputs();

        _validationErrors.Clear();
        _connectors = [];
        _elements = [];

        if (Document is null) return;

        if (SelectedConnectorIds.Length > 0)
        {
            _connectors = SelectedConnectorIds
                .Select(id => Document.Connectors.FirstOrDefault(c => c.Id == id))
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();
            return;
        }

        if (SelectedIds.Length == 0) return;

        _elements = SelectedIds
            .Select(id => Document.Elements.FirstOrDefault(e => e.Id == id))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        if (_elements.Count == 0) return;

        _isMulti  = _elements.Count > 1;
        _sameType = _elements.Select(e => e.Type).Distinct().Count() == 1;

        // Resolve definition(s)
        if (_sameType)
        {
            _def = _registry.GetDef(_elements[0].Type, ComponentScope);
            _commonProps = _def?.Props.ToList() ?? [];
        }
        else
        {
            // Multi-type: intersect props by Name across all defs
            _def = null;
            var defLists = _elements
                .Select(e => _registry.GetDef(e.Type, ComponentScope)?.Props ?? (IReadOnlyList<PropDef>)[])
                .ToList();
            var commonNames = defLists
                .Skip(1)
                .Aggregate(
                    defLists[0].Select(p => p.Name).ToHashSet(),
                    (acc, pl) => { acc.IntersectWith(pl.Select(p => p.Name)); return acc; });
            _commonProps = defLists[0].Where(p => commonNames.Contains(p.Name)).ToList();
        }

        // Categories (props without a category go into "General")
        _propCategories = _commonProps
            .Select(p => p.Category ?? "General")
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        // Layout values
        _layoutX = MixedOrValue(_elements.Select(e => e.X));
        _layoutY = MixedOrValue(_elements.Select(e => e.Y));
        _layoutW = MixedOrValue(_elements.Select(e => e.W));
        _layoutH = MixedOrValue(_elements.Select(e => e.H));
    }

    // ── Canvas / page / grid (empty selection) ────────────────────────────────

    /// <summary>Re-adopts the host's grid values when (and only when) the parameters themselves changed.</summary>
    private void AdoptHostGrid()
    {
        if (_lastShowGridParam != ShowGrid) { _lastShowGridParam = ShowGrid; _showGrid = ShowGrid; }
        if (_lastGridSizeParam != GridSize) { _lastGridSizeParam = GridSize; _gridSize = GridSize; }
    }

    /// <summary>Seeds the width/height inputs from the active page, and again whenever the page or its size changes externally.</summary>
    private void SeedCanvasInputs()
    {
        var page = Document?.ActivePage;
        if (page is null) { _seededPageId = null; return; }
        if (_seededPageId == page.Id && _seededW.Equals(page.Width) && _seededH.Equals(page.Height)) return;

        _canvasSizeInvalid = false;
        _seededPageId = page.Id;
        _seededW = page.Width;
        _seededH = page.Height;
        _canvasWInput = FormatSize(page.Width);
        _canvasHInput = FormatSize(page.Height);
        _customPresetChosen = false;
    }

    private static string FormatSize(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The option the preset select shows: the matching preset of the active page's size, else Custom.</summary>
    private string SelectedPresetId
    {
        get
        {
            var page = Document?.ActivePage;
            if (page is null || _customPresetChosen) return WireframeDevicePresets.CustomId;
            return WireframeDevicePresets.Match(page.Width, page.Height)?.Id ?? WireframeDevicePresets.CustomId;
        }
    }

    private string GetPresetLabel(WireframeDevicePreset preset) => preset.IsCustom
        ? Loc[preset.NameKey]
        : $"{Loc[preset.NameKey]} ({FormatSize(preset.Width!.Value)}×{FormatSize(preset.Height!.Value)})";

    private async Task OnDevicePresetChanged(ChangeEventArgs e)
    {
        if (ReadOnly || Document?.ActivePage is null) return;
        var preset = WireframeDevicePresets.Find(e.Value?.ToString());
        if (preset is null) return;

        if (preset.IsCustom)
        {
            _customPresetChosen = true;   // nothing changes; the width/height inputs are for typing
            return;
        }

        _customPresetChosen = false;
        _canvasSizeInvalid = false;
        await ResizeCanvasAsync(preset.Width!.Value, preset.Height!.Value);
    }

    private async Task OnApplyCanvasSize()
    {
        if (ReadOnly || Document?.ActivePage is null) return;
        if (!double.TryParse(_canvasWInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || w < MinCanvasSize || w > MaxCanvasSize
            || !double.TryParse(_canvasHInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) || h < MinCanvasSize || h > MaxCanvasSize)
        {
            _canvasSizeInvalid = true;   // announced (role=alert) instead of silently doing nothing
            return;
        }

        _canvasSizeInvalid = false;
        await ResizeCanvasAsync(w, h);
    }

    /// <summary>Pushes a <see cref="ResizeCanvasCommand"/> for the active page (skipped when unchanged), then notifies the host.</summary>
    private async Task ResizeCanvasAsync(double w, double h)
    {
        var page = Document?.ActivePage;
        if (Document is null || page is null) return;
        if (Math.Abs(w - page.Width) < 0.5 && Math.Abs(h - page.Height) < 0.5) return;

        var command = new ResizeCanvasCommand(Document, page.Width, page.Height, w, h);
        if (CommandStack is not null) CommandStack.Push(command); else command.Execute();

        SeedCanvasInputs();
        await OnCanvasResized.InvokeAsync((w, h));
        await NotifyChanged();
    }

    private async Task OnPageNameChanged(ChangeEventArgs e)
    {
        var page = Document?.ActivePage;
        if (ReadOnly || Document is null || page is null) return;
        var name = e.Value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(name) || name == page.Name) return;

        page.Name = name;   // page operations are not undoable (same as the page tabs)
        await NotifyChanged();
    }

    private async Task OnShowGridChanged(ChangeEventArgs e)
    {
        _showGrid = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var bv) && bv;
        await ShowGridChanged.InvokeAsync(_showGrid);
    }

    private async Task OnGridSizeChanged(ChangeEventArgs e)
    {
        if (!double.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var raw)) return;
        _gridSize = (int)Math.Clamp(Math.Round(raw), 0, 128);
        await GridSizeChanged.InvokeAsync(_gridSize);
    }

    // ── Element name / delete ─────────────────────────────────────────────────

    private async Task OnElementNameChanged(ChangeEventArgs e)
    {
        if (ReadOnly || Document is null || _elements.Count != 1) return;
        var el = _elements[0];
        var raw = e.Value?.ToString()?.Trim();
        var newName = string.IsNullOrEmpty(raw) ? null : raw;
        if (string.Equals(newName ?? "", el.Name ?? "", StringComparison.Ordinal)) return;

        var command = new RenameElementCommand(Document, el.Id, el.Name, newName);
        if (CommandStack is not null) CommandStack.Push(command); else command.Execute();
        await NotifyChanged();
    }

    private async Task OnDeleteClicked()
    {
        if (ReadOnly || Document is null || _elements.Count == 0) return;
        if (OnDelete.HasDelegate)
        {
            await OnDelete.InvokeAsync();
            return;
        }

        var command = new RemoveElementsCommand(Document, _elements.Select(e => e.Id).ToList());
        if (CommandStack is not null) CommandStack.Push(command); else command.Execute();
        await NotifyChanged();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MixedOrValue(IEnumerable<double> values)
    {
        var list = values.Select(v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                         .Distinct()
                         .ToList();
        return list.Count == 1 ? list[0] : MixedMarker;
    }

    private IEnumerable<PropDef> GetPropsForCategory(string cat)
        => _commonProps.Where(p => (p.Category ?? "General") == cat);

    private string GetCategoryName(string cat) => cat switch
    {
        "General"   => Loc["TmWireframeProps_SectionGeneral"],
        "Content"   => Loc["TmWireframeProps_CatContent"],
        "Appearance"=> Loc["TmWireframeProps_CatAppearance"],
        "Behavior"  => Loc["TmWireframeProps_CatBehavior"],
        "State"     => Loc["TmWireframeProps_CatState"],
        _           => cat,
    };

    /// <summary>True when the prop has different values across the selected elements.</summary>
    internal bool IsMixedValue(string propName)
        => IsMixedValue(_elements, propName);

    /// <summary>Returns the string display value for a prop (first element's value or default).</summary>
    internal string GetDisplayValue(string propName, PropDef prop)
        => GetDisplayValue(_elements, propName, prop);

    // ── Static helpers (testable without a Blazor host) ───────────────────────

    /// <summary>True when the prop has different values across <paramref name="elements"/>.</summary>
    internal static bool IsMixedValue(IList<WireframeElement> elements, string propName)
    {
        if (elements.Count <= 1) return false;
        var values = elements
            .Select(e => e.Props.TryGetValue(propName, out var v) ? v.ToString() : null)
            .Distinct()
            .ToList();
        return values.Count > 1;
    }

    /// <summary>Returns the string display value for a prop from <paramref name="elements"/>[0].</summary>
    internal static string GetDisplayValue(IList<WireframeElement> elements, string propName, PropDef prop)
    {
        if (elements.Count == 0) return prop.Default?.ToString() ?? "";
        var el = elements[0];
        if (!el.Props.TryGetValue(propName, out var je))
            return prop.Default?.ToString() ?? "";

        return je.ValueKind switch
        {
            JsonValueKind.String => je.GetString() ?? "",
            JsonValueKind.Number => je.ToString(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            JsonValueKind.Array  => string.Join(", ", je.EnumerateArray()
                                        .Select(i => i.GetString() ?? i.ToString())),
            _                    => je.ToString()
        };
    }

    // ── Debounce helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Schedules <paramref name="apply"/> after <see cref="DebounceMs"/> of inactivity on
    /// <paramref name="key"/>. Each key has its own CTS so concurrent field edits don't
    /// cancel each other.
    /// </summary>
    private async Task DebounceApply(string key, Func<Task> apply)
    {
        if (_debounceCts.TryGetValue(key, out var old)) { old.Cancel(); old.Dispose(); }
        var cts = new CancellationTokenSource();
        _debounceCts[key] = cts;
        try
        {
            await Task.Delay(DebounceMs, cts.Token);
            if (!cts.IsCancellationRequested) await apply();
        }
        catch (OperationCanceledException) { /* superseded by newer keystroke on the same field */ }
    }

    // ── Change handlers ───────────────────────────────────────────────────────

    // Layout and ZIndex use @oninput → debounce; silent on partial/invalid input
    private Task OnLayoutChanged(string field, ChangeEventArgs e)
    {
        if (Document is null) return Task.CompletedTask;
        var raw = e.Value?.ToString() ?? "";
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
            return Task.CompletedTask;   // user still typing (e.g. "-", "1.")

        return DebounceApply("layout_" + field, async () =>
        {
            foreach (var el in _elements)
            {
                double ax = el.X, ay = el.Y, aw = el.W, ah = el.H;
                switch (field)
                {
                    case "x": ax = val; break;
                    case "y": ay = val; break;
                    case "w": aw = Math.Max(1, val); break;
                    case "h": ah = Math.Max(1, val); break;
                }
                if (CommandStack is not null)
                    CommandStack.Push(new ResizeElementCommand(Document, el.Id, el.X, el.Y, el.W, el.H, ax, ay, aw, ah));
                else
                    { el.X = ax; el.Y = ay; el.W = aw; el.H = ah; }
            }
            await NotifyChanged();
        });
    }

    private Task OnZIndexChanged(ChangeEventArgs e)
    {
        if (Document is null || _elements.Count == 0) return Task.CompletedTask;
        if (!int.TryParse(e.Value?.ToString(), out var z)) return Task.CompletedTask;
        var el = _elements[0];
        var before = el.ZIndex;

        return DebounceApply("zindex", async () =>
        {
            if (CommandStack is not null)
                CommandStack.Push(new ZIndexCommand(Document, el.Id, before, z, Loc["TmWireframeProps_CommandChangeZIndex"]));
            else
                el.ZIndex = z;
            await NotifyChanged();
        });
    }

    // String props: @oninput → validate immediately, debounce commit
    private Task OnStringPropChanged(string propName, ChangeEventArgs e,
        string? validationRegex = null)
    {
        if (Document is null) return Task.CompletedTask;
        var raw = e.Value?.ToString() ?? "";

        _validationErrors.Remove(propName);
        if (!string.IsNullOrEmpty(validationRegex) && !string.IsNullOrEmpty(raw))
        {
            if (!Regex.IsMatch(raw, validationRegex))
            {
                _validationErrors[propName] = Loc["TmWireframeProps_ValidationRegexError", validationRegex];
                return Task.CompletedTask;
            }
        }

        return DebounceApply(propName, async () =>
        {
            await PushPropChange(propName, JsonSerializer.SerializeToElement(raw));
        });
    }

    // Bool checkbox: @onchange → immediate (toggle is atomic)
    private async Task OnBoolPropChanged(string propName, ChangeEventArgs e)
    {
        if (Document is null) return;
        var val = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var bv) && bv;
        await PushPropChange(propName, JsonSerializer.SerializeToElement(val));
    }

    // Number props: @oninput → debounce; silent on partial input (e.g. "-", "1.")
    private Task OnNumberPropChanged(string propName, PropType type, ChangeEventArgs e)
    {
        if (Document is null) return Task.CompletedTask;
        var raw = e.Value?.ToString() ?? "";
        _validationErrors.Remove(propName);

        if (type == PropType.Int)
        {
            if (!int.TryParse(raw, out var i)) return Task.CompletedTask;
            return DebounceApply(propName, () => PushPropChange(propName, JsonSerializer.SerializeToElement(i)));
        }
        else
        {
            if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                return Task.CompletedTask;
            return DebounceApply(propName, () => PushPropChange(propName, JsonSerializer.SerializeToElement(d)));
        }
    }

    // StringList: @oninput → debounce
    private Task OnStringListPropChanged(string propName, ChangeEventArgs e)
    {
        if (Document is null) return Task.CompletedTask;
        var raw = e.Value?.ToString() ?? "";
        var arr = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return DebounceApply(propName, () => PushPropChange(propName, JsonSerializer.SerializeToElement(arr)));
    }

    // ── Command routing ───────────────────────────────────────────────────────

    private async Task PushPropChange(string propName, JsonElement newValue)
    {
        if (Document is null) return;

        if (_elements.Count == 1)
        {
            var changes = new Dictionary<string, JsonElement?> { [propName] = newValue };
            if (CommandStack is not null)
                CommandStack.Push(new UpdatePropsCommand(Document, _elements[0].Id, changes));
            else
                _elements[0].Props[propName] = newValue;

            // When size changes and the component def has size presets, also resize the element.
            if (propName == "size")
                TryApplySizePreset(_elements[0], newValue.GetString());
        }
        else
        {
            var changes = new Dictionary<string, JsonElement?> { [propName] = newValue };
            if (CommandStack is not null)
                CommandStack.Push(new BulkUpdateCommand(Document, _elements.Select(e => e.Id), changes));
            else
                foreach (var el in _elements) el.Props[propName] = newValue;

            // Apply size presets to each element individually (may differ by type)
            if (propName == "size")
                foreach (var el in _elements)
                    TryApplySizePreset(el, newValue.GetString());
        }

        await NotifyChanged();
    }

    private void TryApplySizePreset(WireframeElement el, string? sizeValue)
    {
        if (Document is null || sizeValue is null) return;
        var def = _registry.GetDef(el.Type, ComponentScope);
        if (def?.SizePresets is null) return;
        if (!def.SizePresets.TryGetValue(sizeValue, out var preset)) return;

        if (CommandStack is not null)
            CommandStack.Push(new ResizeElementCommand(
                Document, el.Id, el.X, el.Y, el.W, el.H, el.X, el.Y, preset.W, preset.H));
        else
            { el.W = preset.W; el.H = preset.H; }
    }

    // ── Arrange commands (Bring to Front / Send to Back) ──────────────────────

    private async Task OnBringToFront()
    {
        if (Document is null || _elements.Count == 0) return;
        var ids = _elements.Select(e => e.Id).ToList();
        if (CommandStack is not null)
            CommandStack.Push(new BringToFrontCommand(Document, ids));
        else
        {
            var maxZ = Document.Elements.Max(e => e.ZIndex);
            foreach (var el in _elements)
                el.ZIndex = ++maxZ;
        }
        await NotifyChanged();
    }

    private async Task OnSendToBack()
    {
        if (Document is null || _elements.Count == 0) return;
        var ids = _elements.Select(e => e.Id).ToList();
        if (CommandStack is not null)
            CommandStack.Push(new SendToBackCommand(Document, ids));
        else
        {
            var minZ = Document.Elements.Min(e => e.ZIndex);
            var offset = ids.Count;
            foreach (var el in _elements)
                el.ZIndex = minZ - offset--;
        }
        await NotifyChanged();
    }

    // ── Lock toggle ───────────────────────────────────────────────────────────

    private async Task OnLockChanged(ChangeEventArgs e)
    {
        if (Document is null || _elements.Count == 0) return;
        var val = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var bv) && bv;
        var ids = _elements.Select(e => e.Id).ToList();

        if (CommandStack is not null)
        {
            if (val)
                CommandStack.Push(new LockElementsCommand(Document, ids));
            else
                CommandStack.Push(new UnlockElementsCommand(Document, ids));
        }
        else
        {
            foreach (var el in _elements)
                el.IsLocked = val;
        }
        await NotifyChanged();
    }

    // ── Rotation change ───────────────────────────────────────────────────────

    private Task OnRotationChanged(ChangeEventArgs e)
    {
        if (Document is null || _elements.Count == 0) return Task.CompletedTask;
        if (!double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rot))
            return Task.CompletedTask;

        return DebounceApply("rotation", async () =>
        {
            if (_elements.Count == 1)
            {
                var el = _elements[0];
                var before = el.Rotation;
                if (CommandStack is not null)
                    CommandStack.Push(new RotateElementCommand(Document, el.Id, before, rot));
                else
                    el.Rotation = rot;
            }
            else
            {
                // Multi-select: apply same rotation to all selected
                if (CommandStack is not null)
                {
                    var ids = _elements.Select(e => e.Id);
                    var beforeMap = _elements.ToDictionary(e => e.Id, e => e.Rotation);
                    CommandStack.Push(new BulkRotateCommand(Document, ids, beforeMap, rot));
                }
                else
                {
                    foreach (var el in _elements)
                        el.Rotation = rot;
                }
            }
            await NotifyChanged();
        });
    }

    private async Task NotifyChanged()
    {
        if (Document is not null)
            await DocumentChanged.InvokeAsync(Document);
    }

    // ── Align / Distribute ────────────────────────────────────────────────────

    private async Task OnAlign(WireframeAlignment alignment)
    {
        if (Document is null || _elements.Count < 2) return;
        var ids = _elements.Select(e => e.Id);
        if (CommandStack is not null)
            CommandStack.Push(new AlignElementsCommand(Document, ids, alignment));
        else
            new AlignElementsCommand(Document, ids, alignment).Execute();
        await NotifyChanged();
    }

    private async Task OnDistribute(WireframeDistribution distribution)
    {
        if (Document is null || _elements.Count < 3) return;
        var ids = _elements.Select(e => e.Id);
        if (CommandStack is not null)
            CommandStack.Push(new DistributeElementsCommand(Document, ids, distribution));
        else
            new DistributeElementsCommand(Document, ids, distribution).Execute();
        await NotifyChanged();
    }

    // ── Connector property handlers ───────────────────────────────────────────

    private async Task OnConnectorRoutingChanged(string connectorId, ChangeEventArgs e)
    {
        if (Document is null) return;
        var c = Document.Connectors.FirstOrDefault(x => x.Id == connectorId);
        if (c is null) return;
        var routing = e.Value?.ToString() ?? "straight";
        if (CommandStack is not null)
            CommandStack.Push(new UpdateConnectorRoutingCommand(Document, connectorId, c.Routing, c.Waypoints.ToList(), routing, c.Waypoints.ToList()));
        else
            c.Routing = routing;
        await NotifyChanged();
    }

    private async Task OnConnectorArrowChanged(string connectorId, bool isStart, ChangeEventArgs e)
    {
        if (Document is null) return;
        var c = Document.Connectors.FirstOrDefault(x => x.Id == connectorId);
        if (c is null) return;
        var arrow = e.Value?.ToString() ?? "none";
        var beforeStart = c.StartArrow;
        var beforeEnd = c.EndArrow;
        var afterStart = isStart ? arrow : c.StartArrow;
        var afterEnd = isStart ? c.EndArrow : arrow;
        if (CommandStack is not null)
            CommandStack.Push(new UpdateConnectorStyleCommand(Document, connectorId,
                c.Stroke, c.StrokeWidth, c.StrokeDasharray, beforeStart, beforeEnd,
                c.Stroke, c.StrokeWidth, c.StrokeDasharray, afterStart, afterEnd));
        else
        {
            if (isStart) c.StartArrow = arrow;
            else c.EndArrow = arrow;
        }
        await NotifyChanged();
    }

    private async Task OnConnectorStrokeChanged(string connectorId, ChangeEventArgs e)
    {
        if (Document is null) return;
        var c = Document.Connectors.FirstOrDefault(x => x.Id == connectorId);
        if (c is null) return;
        var stroke = e.Value?.ToString() ?? "#94a3b8";
        if (CommandStack is not null)
            CommandStack.Push(new UpdateConnectorStyleCommand(Document, connectorId,
                c.Stroke, c.StrokeWidth, c.StrokeDasharray, c.StartArrow, c.EndArrow,
                stroke, c.StrokeWidth, c.StrokeDasharray, c.StartArrow, c.EndArrow));
        else
            c.Stroke = stroke;
        await NotifyChanged();
    }

    private async Task OnConnectorStrokeWidthChanged(string connectorId, ChangeEventArgs e)
    {
        if (Document is null) return;
        var c = Document.Connectors.FirstOrDefault(x => x.Id == connectorId);
        if (c is null) return;
        if (!double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var width))
            return;
        width = Math.Max(0.5, Math.Min(20, width));
        if (CommandStack is not null)
            CommandStack.Push(new UpdateConnectorStyleCommand(Document, connectorId,
                c.Stroke, c.StrokeWidth, c.StrokeDasharray, c.StartArrow, c.EndArrow,
                c.Stroke, width, c.StrokeDasharray, c.StartArrow, c.EndArrow));
        else
            c.StrokeWidth = width;
        await NotifyChanged();
    }

    private async Task OnConnectorLabelChanged(string connectorId, ChangeEventArgs e)
    {
        if (Document is null) return;
        var c = Document.Connectors.FirstOrDefault(x => x.Id == connectorId);
        if (c is null) return;
        var label = e.Value?.ToString();
        if (CommandStack is not null)
            CommandStack.Push(new UpdateConnectorLabelCommand(Document, connectorId, c.Label, label));
        else
            c.Label = label;
        await NotifyChanged();
    }

    private async Task OnLayerChanged(ChangeEventArgs e)
    {
        if (Document is null || _elements.Count == 0) return;
        var layerId = e.Value?.ToString() ?? "";
        var elementIds = _elements.Select(el => el.Id).ToArray();

        if (CommandStack is not null)
            CommandStack.Push(new MoveElementsToLayerCommand(Document, elementIds, layerId));
        else
            foreach (var el in _elements) el.LayerId = layerId;

        await NotifyChanged();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var cts in _debounceCts.Values) { cts.Cancel(); cts.Dispose(); }
        _debounceCts.Clear();
    }

    // ── Group label helper ────────────────────────────────────────────────────

    private string GetGroupLabel(string groupId)
    {
        if (Document is null) return groupId;
        var group = Document.Elements.FirstOrDefault(e => e.Id == groupId && e.Type == "__group__");
        if (group is null) return groupId;
        var label = group.Props.GetString("label");
        return string.IsNullOrEmpty(label) ? "Group" : label;
    }
}
