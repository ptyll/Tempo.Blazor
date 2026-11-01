using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Navigation;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Designs PDF signing templates with page overlays, field palette, selection, and field settings.</summary>
public partial class TmPdfTemplateDesigner : IAsyncDisposable
{
    private const double DefaultPageWidth = 1000;
    private const double DefaultPageHeight = 1000;
    private const double MinWidth = 0.02;
    private const double MinHeight = 0.02;
    private static readonly double[] ZoomSteps = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

    private static readonly SigningFieldType[] DefaultAllowedFieldTypes =
    [
        SigningFieldType.Text,
        SigningFieldType.Signature,
        SigningFieldType.Initials,
        SigningFieldType.Date,
        SigningFieldType.Number,
        SigningFieldType.Checkbox,
        SigningFieldType.Radio,
        SigningFieldType.Select,
        SigningFieldType.Multiple,
        SigningFieldType.File,
        SigningFieldType.Image,
        SigningFieldType.Stamp,
        SigningFieldType.Phone
    ];

    private readonly List<SigningField> _fields = [];
    private readonly HashSet<string> _selectedFieldUuids = [];
    private IReadOnlyList<SigningField>? _lastFields;
    private int? _lastPageIndexParameter;
    private SigningFieldType? _drawType;
    private SigningFieldType? _dragType;
    private bool _paletteHandledByPointer;
    private DrawState? _drawState;
    private MoveState? _moveState;
    private ResizeState? _resizeState;
    private ContextMenuState? _contextMenu;
    private bool _contextMenuPending;
    private ContextMenuState? _contextAction;
    private readonly List<SigningField> _clipboardFields = [];
    private readonly Dictionary<string, ElementReference> _pageSurfaceRefs = [];
    private string? _clipboardStatus;

    private IJSObjectReference? _jsModule;
    private IJSObjectReference? _fieldDrag;
    private DotNetObjectReference<TmPdfTemplateDesigner>? _selfRef;
    private ElementReference _workspaceRef;
    private ElementReference _attachedWorkspace;
    private bool _fieldDragAttached;
    private bool _attachingFieldDrag;

    /// <summary>
    /// Blazor move/up listeners. Omitted once the JS session owns the gesture, so a pointer move
    /// does not schedule a render.
    /// </summary>
    private Dictionary<string, object>? WorkspacePointerHandlers =>
        _fieldDragAttached
            ? null
            : new Dictionary<string, object>
            {
                ["onpointermove"] = EventCallback.Factory.Create<PointerEventArgs>(this, HandlePointerMoveAsync),
                ["onpointerup"] = EventCallback.Factory.Create<PointerEventArgs>(this, HandlePointerUpAsync),
            };
    private bool _paletteOpen = true;
    private bool _inspectorDismissed;
    private TmLayoutMode _resolvedLayout = TmLayoutMode.Desktop;
    private TmContextMenu? _contextMenuRef;
    private bool _isDetecting;
    private string? _detectionError;
    private int _currentPageIndex;
    private double _scale = 1.0;
    private DocumentPageZoomMode _zoomMode = DocumentPageZoomMode.Custom;
    private DocumentPageViewMode _viewMode = DocumentPageViewMode.SinglePage;

    /// <summary>Document pages available for template design.</summary>
    [Parameter] public IReadOnlyList<SigningDocumentPage> Documents { get; set; } = [];

    /// <summary>Current signing fields.</summary>
    [Parameter] public IReadOnlyList<SigningField> Fields { get; set; } = [];

    /// <summary>Callback invoked when the signing fields collection changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<SigningField>> FieldsChanged { get; set; }

    /// <summary>Signer roles available for assigning newly created fields.</summary>
    [Parameter] public IReadOnlyList<SigningSubmitterRole> SubmitterRoles { get; set; } = [];

    /// <summary>Currently selected submitter role identifier used for new fields.</summary>
    [Parameter] public string? SelectedSubmitterUuid { get; set; }

    /// <summary>Field types visible in the palette. Defaults to common signing field types.</summary>
    [Parameter] public IReadOnlyList<SigningFieldType>? AllowedFieldTypes { get; set; }

    /// <summary>Whether editing controls should be disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// CSS block size of the designer. Null keeps
    /// <c>var(--tm-pdf-template-designer-height)</c>, which defaults to the historical fixed height.
    /// </summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>
    /// Breakpoints the shell classifies its own container with. Null uses the designer default
    /// (mobile below 768, desktop from 1200), the same steps as the sibling editors.
    /// </summary>
    [Parameter] public TmLayoutBreakpoints? LayoutBreakpoints { get; set; }

    /// <summary>
    /// Whether to force the mobile layout. Obsolete: set <see cref="LayoutMode"/> to
    /// <see cref="TmLayoutMode.Mobile"/>; this flag stays as an alias that maps to it.
    /// </summary>
    [Obsolete("Use LayoutMode: MobileMode stays as an alias that forces TmLayoutMode.Mobile.")]
    [Parameter] public bool MobileMode { get; set; }

    /// <summary>
    /// How the designer decides its layout. <see cref="TmLayoutMode.Auto"/> follows the shell's own
    /// container measurement; a forced value renders that layout without a DOM measurement. Defaults to Auto.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>Current page index used when <see cref="ViewMode"/> is <see cref="DocumentPageViewMode.SinglePage"/>.</summary>
    [Parameter] public int PageIndex { get; set; }

    /// <summary>Callback invoked when the current page index changes.</summary>
    [Parameter] public EventCallback<int> PageIndexChanged { get; set; }

    /// <summary>Current page presentation mode. Defaults to single page for editing precision.</summary>
    [Parameter] public DocumentPageViewMode ViewMode { get; set; } = DocumentPageViewMode.SinglePage;

    /// <summary>Callback invoked when the page presentation mode changes.</summary>
    [Parameter] public EventCallback<DocumentPageViewMode> ViewModeChanged { get; set; }

    /// <summary>Current document zoom scale shared by rendered pages.</summary>
    [Parameter] public double Scale { get; set; } = 1.0;

    /// <summary>Callback invoked when the document zoom scale changes.</summary>
    [Parameter] public EventCallback<double> ScaleChanged { get; set; }

    /// <summary>Current document zoom behavior.</summary>
    [Parameter] public DocumentPageZoomMode ZoomMode { get; set; } = DocumentPageZoomMode.Custom;

    /// <summary>Callback invoked when the document zoom behavior changes.</summary>
    [Parameter] public EventCallback<DocumentPageZoomMode> ZoomModeChanged { get; set; }

    /// <summary>Culture used to preview localized field labels.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Callback invoked when the designer preview culture changes.</summary>
    [Parameter] public EventCallback<string?> CultureChanged { get; set; }

    /// <summary>Fallback culture used when localized field labels are missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Cultures available for previewing and authoring localized field text.</summary>
    [Parameter] public IReadOnlyList<string> SupportedCultures { get; set; } = [];

    /// <summary>Whether to show culture preview and localized text authoring controls.</summary>
    [Parameter] public bool ShowCulturePreview { get; set; }

    /// <summary>Optional async detector that returns fields to add to the template.</summary>
    [Parameter] public Func<Task<IReadOnlyList<SigningField>>>? OnDetectFields { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private IReadOnlyList<SigningFieldType> AllowedTypes => AllowedFieldTypes is { Count: > 0 }
        ? AllowedFieldTypes
        : DefaultAllowedFieldTypes;

    private SigningField? SelectedField => _selectedFieldUuids.Count == 1
        ? _fields.FirstOrDefault(item => _selectedFieldUuids.Contains(item.Uuid))
        : null;

    private int SelectedFieldCount => _selectedFieldUuids.Count(uuid => _fields.Any(item => item.Uuid == uuid));

    private string RootStyle => string.IsNullOrWhiteSpace(Height)
        ? string.Empty
        : $"--tm-pdf-template-designer-height: {Height};";

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-pdf-template-designer" };
            AddClass(classes, _drawType.HasValue, "tm-pdf-template-designer--drawing");
            AddClass(classes, _dragType.HasValue, "tm-pdf-template-designer--dragging");
            AddClass(classes, Disabled, "tm-pdf-template-designer--disabled");
#pragma warning disable CS0618
            AddClass(classes, MobileMode || ResolvedLayout == TmLayoutMode.Mobile, "tm-pdf-template-designer--mobile");
#pragma warning restore CS0618

            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    private static readonly TmLayoutBreakpoints DesignerBreakpoints = new(768, 1200);

    private TmLayoutMode ShellLayoutMode
    {
        get
        {
#pragma warning disable CS0618
            // MobileMode is the obsolete alias. The shell only sees LayoutMode, so the alias has to
            // pin it or a wide test host classifies the designer as desktop.
            if (MobileMode) return TmLayoutMode.Mobile;
#pragma warning restore CS0618
            return LayoutMode;
        }
    }

    private TmLayoutMode ResolvedLayout
    {
        get
        {
#pragma warning disable CS0618
            if (MobileMode) return TmLayoutMode.Mobile;
#pragma warning restore CS0618
            // A forced mode wins. Auto follows the shell's own measurement, reported through
            // ResolvedLayoutChanged — never a JS read of data-layout.
            return LayoutMode != TmLayoutMode.Auto ? LayoutMode : _resolvedLayout;
        }
    }

    private bool InspectorOpen =>
        ResolvedLayout is TmLayoutMode.Tablet or TmLayoutMode.Mobile
            ? SelectedFieldCount > 0 && !_inspectorDismissed
            : true;

    private bool ShowCanvasPalette => ResolvedLayout is TmLayoutMode.Tablet or TmLayoutMode.Mobile;

    /// <summary>
    /// Mobile (G2) action bar: Autodetect, Delete selected, and More. Null off mobile so the shell
    /// keeps the toolbar. Pages and view stay reachable from More because the shell hides the
    /// toolbar once the bar is set.
    /// </summary>
    private IReadOnlyList<TmActionItem>? MobileActionItems =>
        ResolvedLayout == TmLayoutMode.Mobile ? BuildMobileActions() : null;

    private IReadOnlyList<TmActionItem> BuildMobileActions()
    {
        var items = new List<TmActionItem>();
        if (OnDetectFields is not null)
        {
            items.Add(new TmActionItem
            {
                Id = "autodetect",
                TestId = "designer-mobile-detect",
                Icon = "scan-line",
                Label = Loc["TmPdfTemplateDesigner_AutodetectShort"],
                Disabled = _isDetecting,
                OnClick = EventCallback.Factory.Create(this, DetectFieldsAsync)
            });
        }

        items.Add(new TmActionItem
        {
            Id = "delete-selected",
            TestId = "designer-mobile-delete",
            Icon = "trash-2",
            Label = Loc["TmPdfTemplateDesigner_DeleteSelected"],
            Danger = true,
            Disabled = SelectedFieldCount == 0,
            OnClick = EventCallback.Factory.Create(this, DeleteSelectedAsync)
        });
        items.Add(new TmActionItem
        {
            Id = "previous-page",
            TestId = "designer-mobile-prev",
            Label = Loc["TmPdfTemplateDesigner_PreviousPage"],
            Icon = "chevron-left",
            Overflow = ActionOverflow.Always,
            Disabled = !CanGoPrevious,
            OnClick = EventCallback.Factory.Create(this, GoToPreviousPageAsync)
        });
        items.Add(new TmActionItem
        {
            Id = "next-page",
            TestId = "designer-mobile-next",
            Label = Loc["TmPdfTemplateDesigner_NextPage"],
            Icon = "chevron-right",
            Overflow = ActionOverflow.Always,
            Disabled = !CanGoNext,
            OnClick = EventCallback.Factory.Create(this, GoToNextPageAsync)
        });
        items.Add(new TmActionItem
        {
            Id = "fit-width",
            TestId = "designer-mobile-fit-width",
            Label = Loc["TmPdfTemplateDesigner_FitWidth"],
            Icon = "maximize-2",
            Overflow = ActionOverflow.Always,
            OnClick = EventCallback.Factory.Create(this, FitWidthAsync)
        });
        items.Add(new TmActionItem
        {
            Id = "fit-page",
            TestId = "designer-mobile-fit-page",
            Label = Loc["TmPdfTemplateDesigner_FitPage"],
            Icon = "minimize-2",
            Overflow = ActionOverflow.Always,
            OnClick = EventCallback.Factory.Create(this, FitPageAsync)
        });
        if (ShouldShowCulturePreview)
        {
            items.Add(new TmActionItem
            {
                Id = "culture",
                TestId = "designer-mobile-culture",
                Label = Loc["TmPdfTemplateDesigner_CulturePreview"],
                Icon = "languages",
                Overflow = ActionOverflow.Always,
                OnClick = EventCallback.Factory.Create(this, CycleCultureAsync)
            });
        }

        return items;
    }

    /// <summary>
    /// The docked palette is the Left content. On a tablet the rail owns the palette, so Left
    /// must render nothing — an empty fragment still counts as content and opens the drawer.
    /// </summary>
    private bool ShowDockedPalette => !Disabled && Documents.Count > 0 && !ShowCanvasPalette;

    private Task OnShellLayoutChanged(TmLayoutMode mode)
    {
        if (mode == TmLayoutMode.Auto || mode == _resolvedLayout)
        {
            return Task.CompletedTask;
        }

        _resolvedLayout = mode;
        return InvokeAsync(StateHasChanged);
    }

    private Task OnInspectorOpenChanged(bool open)
    {
        if (!open && ResolvedLayout is TmLayoutMode.Tablet or TmLayoutMode.Mobile)
        {
            _inspectorDismissed = true;
        }

        return InvokeAsync(StateHasChanged);
    }

    private bool PaletteOpen => ShowDockedPalette && _paletteOpen;

    private Task OnPaletteOpenChanged(bool open)
    {
        _paletteOpen = open;
        return InvokeAsync(StateHasChanged);
    }

    private Dictionary<string, object> CulturePreviewAttributes => new()
    {
        ["class"] = "tm-pdf-template-designer__culture-preview",
        ["aria-label"] = Loc["TmPdfTemplateDesigner_CulturePreviewAria"]
    };

    private string ViewButtonClass(DocumentPageViewMode mode, string hook)
        => _viewMode == mode
            ? $"tm-pdf-template-designer__toolbar-button--active {hook}"
            : hook;

    private Task HandleCultureValueChangedAsync(string value)
        => HandleCultureChangedAsync(new ChangeEventArgs { Value = value });

    private IReadOnlyList<TmActionItem> CultureMenuItems => DesignerCultures
        .Select(culture => new TmActionItem
        {
            Id = "culture-" + culture,
            Label = GetCultureLabel(culture),
            Checked = string.Equals(culture, CurrentCulture, StringComparison.OrdinalIgnoreCase),
            OnClick = EventCallback.Factory.Create(this, () => HandleCultureValueChangedAsync(culture))
        })
        .ToList();

    private string PaletteClass => ResolvedLayout == TmLayoutMode.Mobile
        ? "tm-pdf-template-designer__palette tm-pdf-template-designer__palette--compact"
        : ResolvedLayout == TmLayoutMode.Tablet
            ? "tm-pdf-template-designer__palette tm-pdf-template-designer__palette--rail"
            : "tm-pdf-template-designer__palette";

    private string ContextMenuStyle => _contextMenu is null
        ? string.Empty
        : string.Create(CultureInfo.InvariantCulture, $"left: {_contextMenu.X}px; top: {_contextMenu.Y}px;");

    private bool HasClipboardFields => _clipboardFields.Count > 0;

    private IReadOnlyList<SigningDocumentPage> VisibleDocuments => _viewMode == DocumentPageViewMode.Continuous
        ? Documents
        : CurrentPage is null ? [] : [CurrentPage];

    private SigningDocumentPage? CurrentPage => Documents.Count == 0
        ? null
        : Documents[Math.Min(Math.Max(_currentPageIndex, 0), Documents.Count - 1)];

    private int CurrentPageNumber => Documents.Count == 0 ? 0 : Math.Min(Math.Max(_currentPageIndex, 0), Documents.Count - 1) + 1;

    private string PageLabel => Loc["TmPdfTemplateDesigner_PageLabel", CurrentPageNumber, Math.Max(1, Documents.Count)];

    private string ZoomLabel => string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(_scale * 100)}%");

    private bool CanGoPrevious => _viewMode == DocumentPageViewMode.SinglePage && _currentPageIndex > 0;

    private bool CanGoNext => _viewMode == DocumentPageViewMode.SinglePage && _currentPageIndex < Documents.Count - 1;

    private bool CanZoomOut => _scale > ZoomSteps[0] + 0.001;

    private bool CanZoomIn => _scale < ZoomSteps[^1] - 0.001;

    private IReadOnlyList<string> DesignerCultures => GetDesignerCultures();

    private bool ShouldShowCulturePreview => ShowCulturePreview && DesignerCultures.Count > 1;

    private string CurrentCulture => NormalizeCulture(Culture) ?? DesignerCultures.FirstOrDefault() ?? CultureInfo.CurrentUICulture.Name;

    protected override void OnParametersSet()
    {
        _scale = Clamp(Scale, ZoomSteps[0], ZoomSteps[^1]);
        _zoomMode = ZoomMode;
        _viewMode = ViewMode;
        if (Documents.Count == 0)
        {
            _currentPageIndex = 0;
        }
        else if (_lastPageIndexParameter != PageIndex && PageIndex >= 0 && PageIndex < Documents.Count)
        {
            _currentPageIndex = PageIndex;
        }
        else
        {
            _currentPageIndex = Math.Min(Math.Max(_currentPageIndex, 0), Documents.Count - 1);
        }
        _lastPageIndexParameter = PageIndex;

        if (!ReferenceEquals(_lastFields, Fields))
        {
            _fields.Clear();
            _fields.AddRange((Fields ?? []).Select(Clone));
            _selectedFieldUuids.RemoveWhere(uuid => _fields.All(field => field.Uuid != uuid));
            _lastFields = Fields;
        }

        EnsureSelectedPageVisible();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        await OpenContextMenuAsync();

        if (Disabled)
        {
            await DetachFieldDragAsync();
            return;
        }

        // A layout switch remounts the canvas, so the session follows the new workspace element.
        // Re-entered while an attach is in flight: that entry owns the workspace, and detaching here
        // would drop the session it is about to keep.
        if (_attachingFieldDrag)
        {
            return;
        }

        if (_fieldDragAttached && _attachedWorkspace.Equals(_workspaceRef))
        {
            return;
        }

        _attachingFieldDrag = true;
        try
        {
            await AttachFieldDragAsync();
        }
        finally
        {
            _attachingFieldDrag = false;
        }
    }

    private async Task AttachFieldDragAsync()
    {
        await DetachFieldDragAsync();

        try
        {
            _jsModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Tempo.Blazor.Signing/js/pdf-template-designer.js");
            _selfRef ??= DotNetObjectReference.Create(this);
            _fieldDrag = await _jsModule.InvokeAsync<IJSObjectReference>("attachFieldDrag", _workspaceRef, _selfRef);
            _attachedWorkspace = _workspaceRef;
            // A real module returns a session whose detach is a function. Loose JS (bUnit) returns a
            // stand-in for every invoke, including a missing export, and that stand-in must not steal
            // the gesture from the Blazor fallback.
            var probe = await _fieldDrag.InvokeAsync<string?>("probe");
            _fieldDragAttached = probe == "pdf-template-designer-pointer";
            if (_fieldDragAttached)
            {
                // Drop the Blazor move/up listeners now that JS owns the gesture. The guard above
                // keeps this render from attaching a second session.
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (JSException)
        {
            // bUnit and prerender have no module; the pointer handlers stay the fallback.
        }
        catch (JSDisconnectedException)
        {
            _fieldDragAttached = false;
        }
        catch (InvalidOperationException)
        {
            // bUnit and prerender have no module; the pointer handlers stay the fallback.
        }
    }

    private async Task DetachFieldDragAsync()
    {
        _fieldDragAttached = false;
        _attachedWorkspace = default;
        var session = _fieldDrag;
        _fieldDrag = null;
        if (session is null)
        {
            return;
        }

        try
        {
            await session.InvokeVoidAsync("detach");
            await session.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // The circuit is already gone, or the session never attached.
        }
    }

    private async Task OpenContextMenuAsync()
    {
        if (_contextMenu is null || _contextMenuRef is null)
        {
            return;
        }

        if (!_contextMenuPending)
        {
            return;
        }

        _contextMenuPending = false;
        _contextMenuRef.Open();
        await Task.CompletedTask;
    }

    private Task OnContextMenuOpenChanged(bool open)
    {
        if (open)
        {
            return Task.CompletedTask;
        }

        // Unmount on close. A menu left mounted reopens itself on the next render, and its
        // trigger stays in the tab order.
        _contextMenu = null;
        _contextMenuPending = false;
        return InvokeAsync(StateHasChanged);
    }


    private RenderFragment RenderPageOverlay(SigningDocumentPage page) => builder =>
    {
        var sequence = 0;
        builder.OpenElement(sequence++, "div");
        builder.AddAttribute(sequence++, "class", "tm-pdf-template-designer__page-surface");
        builder.AddAttribute(sequence++, "data-page-key", GetPageKey(page));
        builder.AddAttribute(sequence++, "onpointerdown", EventCallback.Factory.Create<PointerEventArgs>(this, args => HandlePagePointerDownAsync(page, args)));
        // A press must not move focus: the workspace is a tall focusable, and the browser scrolls it into view
        // before the gesture is measured, which shifts the page under the pointer.
        builder.AddEventPreventDefaultAttribute(sequence++, "onpointerdown", true);
        // pointerdown's default is not the mouse focus: the mousedown that follows still scrolls the canvas
        // to reveal the focused workspace. This is not a drag handler — it only cancels that focus.
        builder.AddEventPreventDefaultAttribute(sequence++, "onmousedown", true);
        // The move listener exists only while a Blazor draw is in progress and JS has not taken
        // the gesture. The capture frame stays at a fixed sequence so adding the listener does not
        // shift it (Blazor cannot remove an ElementReferenceCapture).
        if (!_fieldDragAttached && _drawState is not null)
        {
            builder.AddAttribute(20, "onpointermove", EventCallback.Factory.Create<PointerEventArgs>(this, args => HandlePagePointerMoveAsync(page, args)));
        }
        // pointerup stays even after the JS session attaches. The session owns move and resize, but a
        // draw commits here, and the handler returns immediately when no draw is in progress.
        builder.AddAttribute(21, "onpointerup", EventCallback.Factory.Create<PointerEventArgs>(this, args => HandlePagePointerUpAsync(page, args)));
        builder.AddAttribute(22, "oncontextmenu", EventCallback.Factory.Create<MouseEventArgs>(this, args => OpenPageContextMenu(page, args)));
        builder.AddEventPreventDefaultAttribute(23, "oncontextmenu", true);
        builder.AddElementReferenceCapture(24, reference => _pageSurfaceRefs[GetPageKey(page)] = reference);

        foreach (var field in _fields)
        {
            foreach (var area in GetAreasForPage(field, page))
            {
                var currentField = field;
                var currentArea = area;
                builder.OpenComponent<TmSigningFieldOverlay>(sequence++);
                builder.AddAttribute(sequence++, "Field", currentField);
                builder.AddAttribute(sequence++, "Area", currentArea);
                builder.AddAttribute(sequence++, "RoleColor", GetFieldRoleColor(currentField));
                builder.AddAttribute(sequence++, "Selected", _selectedFieldUuids.Contains(currentField.Uuid));
                // G2: a finger may select and tap-to-place. Move and resize stay tablet and up.
                builder.AddAttribute(sequence++, "Draggable", !Disabled && !_drawType.HasValue && ResolvedLayout != TmLayoutMode.Mobile);
                builder.AddAttribute(sequence++, "Editable", !Disabled && _selectedFieldUuids.Contains(currentField.Uuid) && ResolvedLayout != TmLayoutMode.Mobile);
                builder.AddAttribute(sequence++, "ReadOnly", Disabled);
                builder.AddAttribute(sequence++, "Culture", Culture);
                builder.AddAttribute(sequence++, "FallbackCulture", FallbackCulture);
                builder.AddAttribute(sequence++, "OnClick", EventCallback.Factory.Create<TmSigningFieldOverlayPointerEventArgs>(this, args => SelectFieldAsync(args, false)));
                builder.AddAttribute(sequence++, "OnStartMove", EventCallback.Factory.Create<TmSigningFieldOverlayPointerEventArgs>(this, StartMoveAsync));
                builder.AddAttribute(sequence++, "OnStartResize", EventCallback.Factory.Create<TmSigningFieldOverlayResizeEventArgs>(this, StartResizeAsync));
                builder.AddAttribute(sequence++, "OnContextMenu", EventCallback.Factory.Create<TmSigningFieldOverlayPointerEventArgs>(this, OpenFieldContextMenuAsync));
                builder.AddAttribute(sequence++, "data-field-uuid", currentField.Uuid);
                builder.AddAttribute(sequence++, "data-area-uuid", currentArea.Uuid);
                builder.CloseComponent();
            }
        }

        if (_drawState?.PageKey == GetPageKey(page))
        {
            builder.OpenElement(sequence++, "div");
            builder.AddAttribute(sequence++, "class", "tm-pdf-template-designer__draft");
            builder.AddAttribute(sequence++, "style", GetAreaStyle(_drawState.Area));
            builder.CloseElement();
        }

        var selectedAreas = _fields
            .Where(field => _selectedFieldUuids.Contains(field.Uuid))
            .SelectMany(field => GetAreasForPage(field, page))
            .ToArray();

        if (selectedAreas.Length > 1)
        {
            builder.OpenElement(sequence++, "div");
            builder.AddAttribute(sequence++, "class", "tm-pdf-template-designer__selection-bounds");
            builder.AddAttribute(sequence++, "style", GetRectangleStyle(SigningGeometryHelper.GetSelectionRectangle(selectedAreas)));
            builder.CloseElement();
        }

        builder.CloseElement();
    };

    /// <summary>
    /// The colour of the submitter role that owns <paramref name="field"/>, painted on the field's
    /// overlay through <see cref="TmSigningFieldOverlay.RoleColor"/>. A field whose role carries no
    /// colour gets the palette token of the role's order; a field without a matching role keeps the
    /// primary-tint default.
    /// </summary>
    private string? GetFieldRoleColor(SigningField field)
    {
        return SigningRolePalette.ResolveRoleColor(SubmitterRoles, field.SubmitterUuid);
    }

    private Task SelectFieldTypeAsync(SigningFieldType type)
    {
        if (Disabled)
        {
            return Task.CompletedTask;
        }

        _drawType = type;
        _contextMenu = null;
        return Task.CompletedTask;
    }

    private Task HandlePaletteClickAsync(SigningFieldType type)
    {
        // A real press already committed through pointerup; the click that follows it must not place
        // a second field. bUnit's Click() raises onclick without a pointer, so that path still works.
        if (_paletteHandledByPointer)
        {
            _paletteHandledByPointer = false;
            return Task.CompletedTask;
        }

        return _drawType == type ? PlacePaletteFieldAsync(type) : SelectFieldTypeAsync(type);
    }

    private async Task PlacePaletteFieldAsync(SigningFieldType type)
    {
        if (Disabled || Documents.Count == 0)
        {
            return;
        }

        var page = VisibleDocuments.FirstOrDefault() ?? Documents[Math.Clamp(_currentPageIndex, 0, Documents.Count - 1)];
        var area = CreateDefaultAreaAtPoint(page, type, 0.1, 0.1);
        var field = CreateField(type, area);
        _fields.Add(field);
        _selectedFieldUuids.Clear();
        _selectedFieldUuids.Add(field.Uuid);
        _drawType = type;
        _contextMenu = null;
        await NotifyFieldsChangedAsync();
    }

    private Task StartPaletteDrag(SigningFieldType type)
    {
        // The JS session owns the palette while it is attached. These handlers are the fallback.
        if (Disabled || _fieldDragAttached)
        {
            return Task.CompletedTask;
        }

        _dragType = type;
        _drawType = null;
        _contextMenu = null;
        return Task.CompletedTask;
    }

    private Task EndPaletteDrag()
    {
        _dragType = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// A press that ends on the palette itself is a tap (select the type, or place a default field on
    /// the second tap). A press that ends on a page is a placement and is committed there.
    /// </summary>
    private Task FinishPalettePressAsync(SigningFieldType type)
    {
        if (_fieldDragAttached || _dragType != type)
        {
            return Task.CompletedTask;
        }

        _dragType = null;
        _paletteHandledByPointer = true;
        return _drawType == type ? PlacePaletteFieldAsync(type) : SelectFieldTypeAsync(type);
    }

    private async Task GoToPreviousPageAsync()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        _currentPageIndex--;
        _selectedFieldUuids.RemoveWhere(uuid => !IsFieldOnCurrentPage(uuid));
        await PageIndexChanged.InvokeAsync(_currentPageIndex);
    }

    private async Task GoToNextPageAsync()
    {
        if (!CanGoNext)
        {
            return;
        }

        _currentPageIndex++;
        _selectedFieldUuids.RemoveWhere(uuid => !IsFieldOnCurrentPage(uuid));
        await PageIndexChanged.InvokeAsync(_currentPageIndex);
    }

    private async Task SetViewModeAsync(DocumentPageViewMode viewMode)
    {
        if (_viewMode == viewMode)
        {
            return;
        }

        _viewMode = viewMode;
        await ViewModeChanged.InvokeAsync(_viewMode);
    }

    private Task ZoomOutAsync()
    {
        var next = ZoomSteps.LastOrDefault(value => value < _scale - 0.001);
        return SetScaleAsync(next <= 0 ? ZoomSteps[0] : next, DocumentPageZoomMode.Custom);
    }

    private Task ZoomInAsync()
    {
        var next = ZoomSteps.FirstOrDefault(value => value > _scale + 0.001);
        return SetScaleAsync(next <= 0 ? ZoomSteps[^1] : next, DocumentPageZoomMode.Custom);
    }

    private Task FitWidthAsync() => SetScaleAsync(1.0, DocumentPageZoomMode.FitWidth);

    private Task FitPageAsync() => SetScaleAsync(0.85, DocumentPageZoomMode.FitPage);

    private Task CycleCultureAsync()
    {
        var cultures = DesignerCultures;
        if (cultures.Count < 2)
        {
            return Task.CompletedTask;
        }

        var index = -1;
        for (var i = 0; i < cultures.Count; i++)
        {
            if (string.Equals(cultures[i], CurrentCulture, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        var next = cultures[(index + 1 + cultures.Count) % cultures.Count];
        return HandleCultureValueChangedAsync(next);
    }

    private async Task HandleCultureChangedAsync(ChangeEventArgs args)
    {
        Culture = NormalizeCulture(args.Value?.ToString());
        _contextMenu = null;
        await CultureChanged.InvokeAsync(Culture);
    }

    private async Task SetScaleAsync(double scale, DocumentPageZoomMode zoomMode)
    {
        _scale = Clamp(scale, ZoomSteps[0], ZoomSteps[^1]);
        _zoomMode = zoomMode;
        await ScaleChanged.InvokeAsync(_scale);
        await ZoomModeChanged.InvokeAsync(_zoomMode);
    }

    private async Task SelectFieldAsync(TmSigningFieldOverlayPointerEventArgs args, bool append)
    {
        if (args.Field is null)
        {
            return;
        }

        var useAppend = append || args.MouseEventArgs.CtrlKey || args.MouseEventArgs.MetaKey;
        if (!useAppend)
        {
            _selectedFieldUuids.Clear();
        }

        _inspectorDismissed = false;

        if (useAppend && _selectedFieldUuids.Contains(args.Field.Uuid))
        {
            _selectedFieldUuids.Remove(args.Field.Uuid);
        }
        else
        {
            _selectedFieldUuids.Add(args.Field.Uuid);
            if (_viewMode == DocumentPageViewMode.SinglePage && args.Area is not null)
            {
                await SetCurrentPageToAreaAsync(args.Area);
            }
        }

        _contextMenu = null;
        await Task.CompletedTask;
    }

    private Task StartMoveAsync(TmSigningFieldOverlayPointerEventArgs args)
    {
        if (Disabled || ResolvedLayout == TmLayoutMode.Mobile || args.Field is null || args.Area is null)
        {
            return Task.CompletedTask;
        }

        if (args.MouseEventArgs.CtrlKey || args.MouseEventArgs.MetaKey)
        {
            return Task.CompletedTask;
        }

        if (!_selectedFieldUuids.Contains(args.Field.Uuid))
        {
            _selectedFieldUuids.Clear();
            _selectedFieldUuids.Add(args.Field.Uuid);
        }

        var selected = _fields
            .Where(field => _selectedFieldUuids.Contains(field.Uuid))
            .SelectMany(field => field.Areas.Select(area => new AreaSnapshot(field.Uuid, area.Uuid, Clone(area))))
            .ToArray();

        _moveState = new MoveState(args.MouseEventArgs.ClientX, args.MouseEventArgs.ClientY, selected);
        return Task.CompletedTask;
    }

    private Task StartResizeAsync(TmSigningFieldOverlayResizeEventArgs args)
    {
        if (Disabled || ResolvedLayout == TmLayoutMode.Mobile || args.Field is null || args.Area is null)
        {
            return Task.CompletedTask;
        }

        _selectedFieldUuids.Clear();
        _selectedFieldUuids.Add(args.Field.Uuid);
        _resizeState = new ResizeState(
            args.Field.Uuid,
            args.Area.Uuid,
            Clone(args.Area),
            args.Handle,
            args.MouseEventArgs.ClientX,
            args.MouseEventArgs.ClientY);
        return Task.CompletedTask;
    }

    private void ApplyMove(double deltaX, double deltaY)
    {
        if (_moveState is null)
        {
            return;
        }

        foreach (var snapshot in _moveState.Areas)
        {
            var field = _fields.FirstOrDefault(item => item.Uuid == snapshot.FieldUuid);
            var area = field?.Areas.FirstOrDefault(item => item.Uuid == snapshot.AreaUuid);
            if (field is null || area is null)
            {
                continue;
            }

            ReplaceArea(field, area, SigningGeometryHelper.Move(snapshot.Area, deltaX, deltaY, MinWidth, MinHeight));
        }
    }

    private void ApplyResize(double deltaX, double deltaY)
    {
        if (_resizeState is null)
        {
            return;
        }

        var field = _fields.FirstOrDefault(item => item.Uuid == _resizeState.FieldUuid);
        var area = field?.Areas.FirstOrDefault(item => item.Uuid == _resizeState.AreaUuid);
        if (field is null || area is null)
        {
            return;
        }

        ReplaceArea(field, area, SigningGeometryHelper.Resize(_resizeState.Area, _resizeState.Handle, deltaX, deltaY, MinWidth, MinHeight));
    }

    private void SelectFieldsInRectangle(SigningDocumentPage page, SigningFieldArea selection)
    {
        _selectedFieldUuids.Clear();
        foreach (var field in _fields)
        {
            if (GetAreasForPage(field, page).Any(area => Intersects(area, selection)))
            {
                _selectedFieldUuids.Add(field.Uuid);
                _inspectorDismissed = false;
            }
        }
    }

    private async Task HandleEditorFieldChangedAsync(SigningField field)
    {
        var index = _fields.FindIndex(item => item.Uuid == field.Uuid);
        if (index >= 0)
        {
            _fields[index] = Clone(field);
            await NotifyFieldsChangedAsync();
        }
    }

    private async Task DetectFieldsAsync()
    {
        if (OnDetectFields is null)
        {
            return;
        }

        _isDetecting = true;
        _detectionError = null;

        try
        {
            var detected = await OnDetectFields.Invoke();
            _fields.AddRange(detected.Select(Clone));
            await NotifyFieldsChangedAsync();
        }
        catch (Exception ex)
        {
            _detectionError = ex.Message;
        }
        finally
        {
            _isDetecting = false;
        }
    }

    private async Task NotifyFieldsChangedAsync()
    {
        var materialized = _fields.Select(Clone).ToArray();
        _lastFields = materialized;
        if (FieldsChanged.HasDelegate)
        {
            await FieldsChanged.InvokeAsync(materialized);
        }
    }

    private SigningField CreateField(SigningFieldType type, SigningFieldArea area)
    {
        var label = GetFieldTypeLabel(type);
        var labels = new SigningLocalizedText { Default = label };
        if (!string.IsNullOrWhiteSpace(CurrentCulture))
        {
            labels.Translations[CurrentCulture] = label;
        }

        return new SigningField
        {
            Uuid = Guid.NewGuid().ToString("N"),
            SubmitterUuid = SelectedSubmitterUuid ?? SubmitterRoles.FirstOrDefault()?.Uuid,
            Name = label,
            Labels = labels,
            Type = type,
            Required = type is SigningFieldType.Signature or SigningFieldType.Initials,
            Areas = [SigningGeometryHelper.Clamp(area, MinWidth, MinHeight)]
        };
    }

    private IEnumerable<SigningFieldArea> GetAreasForPage(SigningField field, SigningDocumentPage page)
    {
        return field.Areas.Where(area => area.AttachmentUuid == page.AttachmentUuid && area.Page == page.PageIndex);
    }

    private bool IsFieldOnCurrentPage(string fieldUuid)
    {
        if (_viewMode == DocumentPageViewMode.Continuous || CurrentPage is null)
        {
            return true;
        }

        var field = _fields.FirstOrDefault(item => item.Uuid == fieldUuid);
        return field?.Areas.Any(area => area.AttachmentUuid == CurrentPage.AttachmentUuid && area.Page == CurrentPage.PageIndex) == true;
    }

    private void EnsureSelectedPageVisible()
    {
        if (_viewMode != DocumentPageViewMode.SinglePage || _selectedFieldUuids.Count != 1)
        {
            return;
        }

        var field = _fields.FirstOrDefault(item => _selectedFieldUuids.Contains(item.Uuid));
        var area = field?.Areas.FirstOrDefault();
        if (area is null || IsAreaOnCurrentPage(area))
        {
            return;
        }

        var pageIndex = Documents.ToList().FindIndex(page => page.AttachmentUuid == area.AttachmentUuid && page.PageIndex == area.Page);
        if (pageIndex >= 0)
        {
            _currentPageIndex = pageIndex;
        }
    }

    private async Task SetCurrentPageToAreaAsync(SigningFieldArea area)
    {
        if (IsAreaOnCurrentPage(area))
        {
            return;
        }

        var pageIndex = Documents.ToList().FindIndex(page => page.AttachmentUuid == area.AttachmentUuid && page.PageIndex == area.Page);
        if (pageIndex < 0)
        {
            return;
        }

        _currentPageIndex = pageIndex;
        await PageIndexChanged.InvokeAsync(_currentPageIndex);
    }

    private bool IsAreaOnCurrentPage(SigningFieldArea area)
    {
        return CurrentPage is not null && area.AttachmentUuid == CurrentPage.AttachmentUuid && area.Page == CurrentPage.PageIndex;
    }

    private string GetFieldTypeLabel(SigningFieldType type)
    {
        return SigningTextResolver.FieldTypeLabel(type, Loc);
    }

    private static string GetIconName(SigningFieldType type)
    {
        // Signing redesign phase 3: the shared map (overlay + designer + runner field list).
        return SigningFieldTypeIcons.Get(type);
    }

    private static string GetPageKey(SigningDocumentPage page) => $"{page.AttachmentUuid}:{page.PageIndex}";

    private static string GetPageElementId(SigningDocumentPage page) => $"tm-pdf-template-designer-page-{page.AttachmentUuid}-{page.PageIndex}";

    private static void AddClass(List<string> classes, bool condition, string cssClass)
    {
        if (condition)
        {
            classes.Add(cssClass);
        }
    }

    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);
}
