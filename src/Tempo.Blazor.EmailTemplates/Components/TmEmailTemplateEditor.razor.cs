using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Services;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.EmailTemplates.Components;

// The editor's public surface: parameters, the public API and the component lifecycle (N270/F13 —
// the markup file and the behaviour files stay under the 1200-line ceiling; it is one partial class).
public partial class TmEmailTemplateEditor
{
    /// <summary>Gets or sets the edited document. Two-way bindable via <see cref="DocumentChanged"/>.</summary>
    [Parameter] public EmailTemplateDocument? Document { get; set; }

    /// <summary>Raised when the document changes (for <c>@bind-Document</c>).</summary>
    [Parameter] public EventCallback<EmailTemplateDocument> DocumentChanged { get; set; }

    /// <summary>
    /// Optional host save handler. It receives a deep-copy snapshot with the trigger, the editor revision
    /// and the editor's <see cref="CancellationToken"/>, and answers with an
    /// <see cref="EmailSaveResult"/>. The host <b>must</b> answer — a handler that never completes leaves the
    /// editor in <see cref="IsSaving"/> forever; pass <see cref="EmailSaveRequest.CancellationToken"/> to the
    /// host's own I/O and keep its own timeout for it (the token is cancelled when the editor is disposed).
    /// The editor only clears the dirty flag and shows "Saved" after <see cref="EmailSaveResult.Saved"/>; an
    /// exception is reported as a failed save and logged. When <see langword="null"/>, <see cref="SaveAsync"/>
    /// returns a failed result without calling anything.
    /// </summary>
    [Parameter] public Func<EmailSaveRequest, Task<EmailSaveResult>>? OnSave { get; set; }

    /// <summary>
    /// Auto-save configuration; when enabled, a dirty document is saved through <see cref="OnSave"/> with
    /// <see cref="EmailSaveTrigger.AutoSave"/> after the debounce interval. A clean document is never sent,
    /// and a conflict pauses auto-save until the next successful non-auto-save save (manual button,
    /// Ctrl/⌘+S or the host's <see cref="SaveAsync"/>) or a new document reference.
    /// </summary>
    [Parameter] public AutoSaveOptions? AutoSave { get; set; }

    /// <summary>
    /// Raised when <see cref="IsDirty"/> changes — only on a transition, never on initialization and never
    /// for an edit that keeps the document dirty.
    /// </summary>
    [Parameter] public EventCallback<bool> IsDirtyChanged { get; set; }

    /// <summary>
    /// Disables every editing path (toolbox, canvas drag&amp;drop and inline actions, inspector inputs,
    /// import, undo/redo and the shortcuts that mutate the document) while keeping preview, validation and
    /// export available. <see cref="SaveAsync"/> returns a failed result without calling the host. The
    /// component never changes this value itself.
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Optional host actions rendered at the start of the command bar (back, title, …).</summary>
    [Parameter] public RenderFragment? ToolbarStart { get; set; }

    /// <summary>Optional host actions rendered at the end of the command bar, before the save chrome.</summary>
    [Parameter] public RenderFragment? ToolbarEnd { get; set; }

    /// <summary>Whether the Save button is rendered. Defaults to <see langword="true"/>.</summary>
    [Parameter] public bool ShowSaveButton { get; set; } = true;

    /// <summary>
    /// Whether the save-status badge is rendered. Defaults to <see langword="true"/>; the badge announces
    /// unsaved changes, the in-flight save, the relative time of the last confirmed save, a failure with
    /// the host's message and a conflict.
    /// </summary>
    [Parameter] public bool ShowSaveStatus { get; set; } = true;

    /// <summary>
    /// Client-side limits (<c>Accept</c>, <c>MaxBytes</c>) applied to a picked image <b>before</b>
    /// <see cref="OnImageUpload"/> is called. <see langword="null"/> uses the defaults (5 MB, images only).
    /// </summary>
    [Parameter] public ImageUploadOptions? ImageUpload { get; set; }

    /// <summary>Clock used for the auto-save debounce and the relative "saved" time (overridable in tests).</summary>
    [Parameter] public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Host-provided variables offered by the variable picker (merged with extracted ones).</summary>
    [Parameter] public IReadOnlyList<TemplateVariableInfo> Variables { get; set; } = Array.Empty<TemplateVariableInfo>();

    /// <summary>Optional host image-upload handler: receives a picked file, returns the hosted URL.</summary>
    [Parameter] public Func<IBrowserFile, Task<string>>? OnImageUpload { get; set; }

    /// <summary>
    /// Optional host actions rendered at the end of the preview dialog's footer — for example "Send test
    /// email". The editor never sends anything itself; the host owns that action and its result.
    /// </summary>
    [Parameter] public RenderFragment? FooterActions { get; set; }

    /// <summary>
    /// How the items of a social / navbar / carousel / accordion block are edited (§3.9), forwarded to the
    /// inspector. <see cref="EmailItemEditingMode.Inline"/> is the default and keeps the 2.9.x behaviour;
    /// <see cref="EmailItemEditingMode.Dialog"/> edits one item at a time in a modal over a copy, so the
    /// document changes in one undo step and a half-typed item never touches it.
    /// </summary>
    [Parameter] public EmailItemEditingMode ItemEditingMode { get; set; } = EmailItemEditingMode.Inline;

    /// <summary>
    /// Whether the WYSIWYG canvas may request the document's remote images from the AUTHOR's browser
    /// (Fáze 9A). The default is <see cref="Components.CanvasImageLoading.Load"/>: the canvas is a preview
    /// and the sent email loads the same source anyway.
    /// <para>
    /// Set <see cref="Components.CanvasImageLoading.Placeholder"/> for privacy: an imported template can
    /// carry a per-recipient tracking pixel, and the placeholder mode issues no request at all — it paints
    /// a framed box with the image's alt text instead. Only allow-listed URLs are ever requested in
    /// <see cref="Components.CanvasImageLoading.Load"/> mode (the pipeline's URL policy decides).
    /// </para>
    /// </summary>
    [Parameter] public CanvasImageLoading CanvasImageLoading { get; set; } = CanvasImageLoading.Load;

    /// <summary>
    /// Whether the blocks palette is collapsed. The editor mirrors the state it is given and reports every
    /// USER collapse/expand through <see cref="ToolboxCollapsedChanged"/>; F8 maps the same parameter onto
    /// <c>TmEditorShell.CollapsedPanels</c> (D10).
    /// <para>
    /// The tablet auto-rail is a layout state and does NOT raise
    /// <see cref="ToolboxCollapsedChanged"/>: when the shell rails the palette because a tablet layout
    /// expands only one panel, the editor neither adopts that rail into this parameter nor reports it (U1,
    /// D27). Only a user collapse/expand does — so binding this parameter two-way never pins a rail the
    /// layout imposed.
    /// </para>
    /// </summary>
    [Parameter] public bool ToolboxCollapsed { get; set; }

    /// <summary>
    /// Raised with the new state when the USER collapses or expands the blocks palette. Bind it back into
    /// <see cref="ToolboxCollapsed"/> for a controlled palette; without a binding the palette toggles on its
    /// own (seeded from the parameter). A rail the LAYOUT imposed (the tablet one-expanded-panel rule) is a
    /// layout state and never raises this callback (U1): the event reports a preference, not a layout.
    /// </summary>
    [Parameter] public EventCallback<bool> ToolboxCollapsedChanged { get; set; }

    /// <summary>
    /// The limits every entry point applies to a template document (Fáze 10, D13): the import dialog measures
    /// the pasted or uploaded input against <see cref="EmailImportOptions.MaxInputBytes"/> (UTF-8 bytes) and
    /// hands the section/block/depth limits to the importer and the JSON deserializer, and the validation panel
    /// reports the same limits. <see langword="null"/> (default) means the contract values (5 MB, 200 sections,
    /// 2000 blocks, nesting 8). Do not tighten it: the default is today's behaviour (B14).
    /// </summary>
    [Parameter] public EmailImportOptions? ImportOptions { get; set; }

    /// <summary>
    /// The layout the editor renders. <see cref="TmLayoutMode.Auto"/> (default) lets the core
    /// <c>TmEditorShell</c> measure the EDITOR'S OWN CONTAINER — never the viewport — and follow its
    /// width; a forced value renders that mode without a DOM, which is how a host (and a test) pins a
    /// tablet or phone layout. The editor keeps no copy of the breakpoint logic: this value is
    /// forwarded to the shell unchanged.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// The layout rendered before the shell's first measurement. Defaults to
    /// <see cref="TmLayoutMode.Desktop"/> (a desktop host never sees a wrong compact frame).
    /// </summary>
    [Parameter] public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;

    /// <summary>
    /// Raised when the shell's RESOLVED layout changes (the container crossed a breakpoint, or the host
    /// forced another <see cref="LayoutMode"/>). The initial value is not a change. Never bind this to
    /// <see cref="LayoutMode"/> — that would pin the layout and stop the shell from measuring.
    /// </summary>
    [Parameter] public EventCallback<TmLayoutMode> ResolvedLayoutChanged { get; set; }

    /// <summary>
    /// The two container widths that decide the layout, forwarded to the shell. <see langword="null"/>
    /// (default) uses <c>new TmLayoutBreakpoints(768, 1200)</c> (D7): below 768 the editor is a phone
    /// layout, from 1200 it is the full desktop chrome, in between it is the tablet layout — the pair the
    /// DiagramEditor uses, chosen because the toolbox and the inspector need the wider canvas before the
    /// editor may drop a panel.
    /// </summary>
    [Parameter] public TmLayoutBreakpoints? Breakpoints { get; set; }

    /// <summary>
    /// How the two panels present on a mobile layout. <see cref="Tempo.Blazor.Components.Layout.MobilePanelPresentation.Sheet"/>
    /// (default, EM3) keeps the canvas visible with a bottom sheet — a Bloky | Vlastnosti tab strip inside
    /// it while both panels are open; <see cref="Tempo.Blazor.Components.Layout.MobilePanelPresentation.Tabs"/>
    /// is a full-region Bloky | Plátno | Vlastnosti tablist with no sheet.
    /// </summary>
    [Parameter] public MobilePanelPresentation MobilePanelPresentation { get; set; }

    /// <summary>
    /// The panel the mobile layout shows, two-way with <see cref="ActiveMobilePanelChanged"/>. The editor
    /// switches it to <see cref="EditorShellPanel.Right"/> when the user selects a block (the inspector is
    /// where that selection is edited), and a host may drive it too.
    /// </summary>
    [Parameter] public EditorShellPanel ActiveMobilePanel { get; set; }

    /// <summary>Raised when the user picks another mobile tab, or when the editor follows a selection.</summary>
    [Parameter] public EventCallback<EditorShellPanel> ActiveMobilePanelChanged { get; set; }

    /// <summary>
    /// Gets whether the document differs from the last state the host confirmed (or the document it handed
    /// over). Owned by the editor — it is not a <c>[Parameter]</c>, so <c>@bind-IsDirty</c> is not used;
    /// observe changes through <see cref="IsDirtyChanged"/>.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>Gets whether a save request is in flight (at most one at a time).</summary>
    public bool IsSaving { get; private set; }

    private readonly object _saveSync = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private EditorHistoryService _history = new();
    private readonly ClipboardService _clipboard = new();

    private EmailTemplateDocument _document = new();
    private string _baselineJson = string.Empty;
    private long _revision;

    /// <summary>
    /// Bumped whenever the editor throws its state away and adopts a new document from the host. A save that
    /// was in flight against the previous document compares its captured epoch and only answers its caller;
    /// it must not move the baseline, the badge or the auto-save pause of a document it never saw.
    /// </summary>
    private long _documentEpoch;

    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// The last <see cref="Document"/> reference the HOST handed over. A re-render with that same instance
    /// (including a host that only passes <see cref="Document"/> one way and listens to
    /// <see cref="IsDirtyChanged"/>) is not a decision and must not reset history or baseline.
    /// </summary>
    private EmailTemplateDocument? _hostDocument;

    /// <summary>
    /// The last reference the editor itself put on the wire through <see cref="DocumentChanged"/>. A host
    /// that echoes it back (synchronously or after its own await) is not a new document either — comparing
    /// against the live <see cref="_document"/> would mistake an undo/redo snapshot for a host reload.
    /// </summary>
    private EmailTemplateDocument? _emittedDocument;

    private Guid? _selectedId;
    // The inspector's remembered state (collapsed groups, open variable picker + its filter). It lives on the
    // editor - not on the inspector components - because a layout flip re-creates those (decision
    // EMAIL-VARIABLE-PICKER-FILTER: the editor gets NO new parameter, the contract stays as it was).
    private readonly EmailInspectorState _inspectorState = new();
    private bool? _toolboxCollapsedInternal;
    // The shell's OWN state, mirrored only so the parameters keep their two-way contract (the shell
    // raises a callback and a host is free to ignore it).
    private TmLayoutMode? _measuredLayout;
    private EditorShellPanel? _activeMobilePanel;
    // The last ActiveMobilePanel parameter value the editor adopted (the shell's own pattern).
    private EditorShellPanel _activeParamSeen = (EditorShellPanel)(-1);
    private bool _mobileOverflowOpen;
    private bool _leftOpen = true;
    private bool _rightOpen = true;
    // Set by a structural change, consumed in OnAfterRenderAsync: the block the caller wants focused.
    private Guid? _focusBlockAfterRender;
    // Set by a rail expansion, consumed in OnAfterRenderAsync: which expanded panel's toggle takes the
    // focus the unmounted rail button lost. Only the inspector rail is host-owned — the palette rail is
    // the shell's own toggle, and the shell restores that focus itself.
    private string? _focusPanelToggleAfterRender;
    // The layout generation the collapse bridge compares against (U2): a raise "in the layout's batch"
    // is a layout state. See Layout.cs.
    private TmLayoutMode? _layoutModeParamSeen;
    private int _sheetSnapIndex;
    private object? _dragPayload;
    private ITimer? _autoSaveTimer;
    private ITimer? _savedAgeTimer;

    private bool _saveInProgress;
    private PendingSave? _followUpJob;
    private EmailSaveStatus? _lastSaveOutcome;
    private string? _saveErrorMessage;
    private string? _conflictMessage;
    private DateTimeOffset? _lastSavedAt;
    private bool _autoSavePaused;
    private string _announcement = string.Empty;
    private TmEmailTemplateCanvas? _canvas;
    private ITmRelativeTimeFormatter? _relativeTime;

    private ElementReference _rootRef;
    private IJSObjectReference? _shortcutModule;
    private IJSObjectReference? _panelFocusModule;

    private bool _showExport;
    private bool _showImport;
    private bool _showPreview;
    private bool _showValidation;
    private bool _showShortcuts;
    private TmEmailTemplatePreview? _preview;

    /// <summary>
    /// Saves the document through <see cref="OnSave"/> — the hook a host wires into
    /// <c>TmNavigationGuard.OnSaveAndLeave</c> (<c>OnSaveAndLeave="@(async () => (await editor.SaveAsync()).IsSaved)"</c>).
    /// A trigger that arrives while another save runs merges into one follow-up save and this call returns
    /// that follow-up's result. Read-only editors and editors without a save handler answer a failed result
    /// without calling the host.
    /// </summary>
    public async Task<EmailSaveResult> SaveAsync()
    {
        // A host calls this from wherever its own flow runs (a navigation guard, a command handler, a
        // background continuation) — i.e. possibly off the renderer's dispatcher. The save mutates component
        // state and renders, so it runs inside InvokeAsync; the caller still gets the host's own answer.
        EmailSaveResult result = EmailSaveResult.Failed(Loc["EmailEditor_SaveStatus_Cancelled"]);
        await InvokeAsync(async () =>
        {
            result = await RequestSaveAsync(EmailSaveTrigger.Programmatic);
        });
        return result;
    }

    /// <summary>Forces a new preview render, cancelling the one that may still be in flight.</summary>
    public Task RefreshPreviewAsync()
        => _preview is null ? Task.CompletedTask : _preview.RefreshAsync();

    private void ClosePreview() => _showPreview = false;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // Layout bookkeeping runs on EVERY parameter pass, including the first one and a pass whose
        // Document is null: the shell resolves the forced mode from the same values, so the generation
        // must move before its collapse callback arrives (U2), and a one-way ActiveMobilePanel must be
        // adopted whatever the document is doing.
        ApplyLayoutModeParameter();
        ApplyActiveMobilePanelParameter();

        if (!_initialized)
        {
            _initialized = true;
            _document = Document ?? new EmailTemplateDocument();
            _hostDocument = Document;
            _history = new EditorHistoryService(TimeProvider);
            _history.Initialize(_document);
            _baselineJson = ContentSignature(_document);
            return;
        }

        if (Document is null) return;

        // Only a reference the HOST has not handed over before can be a decision (it reloaded, resolved a
        // conflict or switched templates). A one-way host re-renders with the same instance after every
        // IsDirtyChanged; treating that as a reload would wipe the undo history mid-edit.
        if (ReferenceEquals(Document, _hostDocument)) return;
        _hostDocument = Document;

        // The editor's own echo: the host stored the instance the editor emitted and passed it straight
        // back. Its undo/redo snapshots are NOT host reloads.
        if (ReferenceEquals(Document, _emittedDocument)) return;
        if (ReferenceEquals(Document, _document)) return;

        ResetToHostDocument(Document);
    }

    /// <summary>
    /// Adopts a new document reference from the host: a fresh baseline, a cleared dirty flag, no pending
    /// conflict and a new undo history. The document epoch moves so a save still in flight cannot report its
    /// outcome onto this document.
    /// </summary>
    private void ResetToHostDocument(EmailTemplateDocument document)
    {
        _document = document;
        _documentEpoch++;
        _selectedId = null;
        _dragPayload = null;
        _history = new EditorHistoryService(TimeProvider);
        _history.Initialize(_document);
        _baselineJson = ContentSignature(_document);
        _inspectorState.CloseVariables();
        _revision++;
        _lastSaveOutcome = null;
        _saveErrorMessage = null;
        _conflictMessage = null;
        _lastSavedAt = null;
        _announcement = string.Empty;
        _autoSavePaused = false;
        SetDirty(false);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) await AttachSaveShortcutAsync();

        // A structural change armed the canvas focus; it runs AFTER the render, because the element
        // reference only exists once the new block is in the DOM (a focus call during the handler would
        // find the old tree — the block the user just inserted was not in _blockRefs yet).
        if (_focusBlockAfterRender is { } target)
        {
            _focusBlockAfterRender = null;
            await FocusCanvasAsync(target);
        }

        // A rail expansion armed the focus recovery for the panel's own toggle: the rail button is gone
        // by now, and the expanded toggle only exists after this render (review item 18c).
        if (_focusPanelToggleAfterRender is { } panelSide)
        {
            _focusPanelToggleAfterRender = null;
            await FocusExpandedPanelToggleAsync(panelSide);
        }

        SettleLayoutGeneration();
    }

    private ILogger? Logger => Services.GetService(typeof(ILogger<TmEmailTemplateEditor>)) as ILogger<TmEmailTemplateEditor>;

    private void LogError(Exception exception, string message)
        => Logger?.LogError(exception, "{Message}", message);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
        _savedAgeTimer?.Dispose();
        _savedAgeTimer = null;

        // The token lives as long as the host's in-flight work needs it, so the source is cancelled but
        // not disposed: a disposed token throws on registration.
        if (!_disposeCts.IsCancellationRequested)
        {
            try { _disposeCts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        DetachSaveShortcut();
        DisposePanelFocus();
    }

    /// <summary>One queued save request and the caller waiting for its result.</summary>
    private sealed class PendingSave
    {
        // Asynchronous continuations: the queue completes this from PumpAsync, and the awaiting caller then
        // renders/awaits on its own context. Without the flag the completion can run the caller's
        // continuation inline on the save pipeline's stack, which re-enters the component mid-save.
        private readonly TaskCompletionSource<EmailSaveResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingSave(EmailSaveTrigger trigger) => Trigger = trigger;

        public EmailSaveTrigger Trigger { get; private set; }

        public Task<EmailSaveResult> Completion => _completion.Task;

        public void Complete(EmailSaveResult result) => _completion.TrySetResult(result);

        /// <summary>A host-visible manual/programmatic save outranks an auto-save in the merged request.</summary>
        public void Merge(EmailSaveTrigger trigger)
        {
            if (Rank(trigger) > Rank(Trigger)) Trigger = trigger;
        }

        private static int Rank(EmailSaveTrigger trigger) => trigger switch
        {
            EmailSaveTrigger.Programmatic => 3,
            EmailSaveTrigger.Manual => 2,
            EmailSaveTrigger.Shortcut => 1,
            _ => 0,
        };
    }
}
