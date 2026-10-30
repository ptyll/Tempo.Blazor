using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The Fáze 7 mobile half of <see cref="TmModelingEditor"/> (G2 — viewing + light edits), kept in
/// its own partial so the main code-behind respects the source ceiling: the compact panel-tab
/// state (a real tablist whose panels are Preview / Tree / Inspector / Issues), the "Change
/// view" sheet state and the in-flow F5 action bar items. Dragging from the tree into the
/// preview and full editing stay tablet+ — the mobile open-editor dialog presents the diagram
/// editor's own mobile mode.
/// </summary>
public partial class TmModelingEditor
{
    /// <summary>Whether the "Change view" sheet (provider / notation / viewpoint selects) is open.
    /// A mobile-only surface — it can only be opened from the compact DOM.</summary>
    private bool _changeViewOpen;

    /// <summary>The issues dock flag of the mobile band: the Issues TAB shows the whole panel
    /// expanded (unlike the tablet band, which starts collapsed).</summary>
    private bool _mobileIssuesCollapsed;

    /// <summary>Whether the editor's own container currently resolves the mobile band.</summary>
    private bool IsMobileLayout => _resolvedLayout == TmLayoutMode.Mobile;

    /// <summary>G2 scope: the tree→preview drag is a desktop/tablet affordance. On mobile the tree rows
    /// are not draggable and the preview is not a drop target (<c>AllowNodeDrop</c> stays the
    /// host's switch — the mobile band just cannot express it). The non-drag insert is a
    /// DESKTOP/TABLET alternative to that drag: the mobile tree renders neither the "+" button
    /// nor the drag grip — G2 viewing, edits go through "Open in editor" (final review).</summary>
    private bool EffectiveAllowNodeDrop => AllowNodeDrop && !IsMobileLayout;

    /// <summary>The TestIdPrefix the cached mobile attribute dictionaries were built with.</summary>
    private string? _cachedMobileTestIdPrefix;

    /// <summary>Per-instance attributes of the mobile panel tabs strip (the preserved test id).</summary>
    private Dictionary<string, object> MobilePanelTabsAttributes => BuildMobileAttributes(ref _mobilePanelTabsAttributes);

    private Dictionary<string, object>? _mobilePanelTabsAttributes;

    /// <summary>Per-tab test ids of the mobile tab strip (the frozen tab test ids the docked
    /// layouts' tablet row also carries).</summary>
    private IReadOnlyDictionary<string, object>? PreviewTabAttributes => BuildMobileAttributes(ref _previewTabAttributes);

    private Dictionary<string, object>? _previewTabAttributes;

    private IReadOnlyDictionary<string, object>? TreeTabAttributes => BuildMobileAttributes(ref _treeTabAttributes);

    private Dictionary<string, object>? _treeTabAttributes;

    private IReadOnlyDictionary<string, object>? InspectorTabAttributes => BuildMobileAttributes(ref _inspectorTabAttributes);

    private Dictionary<string, object>? _inspectorTabAttributes;

    private IReadOnlyDictionary<string, object>? IssuesTabAttributes => BuildMobileAttributes(ref _issuesTabAttributes);

    private Dictionary<string, object>? _issuesTabAttributes;

    /// <summary>Attributes of the "Change view" sheet overlay (the sheet test id).</summary>
    private Dictionary<string, object> ChangeViewSheetAttributes => BuildMobileAttributes(ref _changeViewSheetAttributes);

    private Dictionary<string, object>? _changeViewSheetAttributes;

    /// <summary>
    /// Returns the cached dictionary, rebuilding ALL of the mobile ones when the
    /// <see cref="TmComponentBase.TestIdPrefix"/> changed (the same pattern the open-editor
    /// modal attributes use) — the test ids stay prefix-aware.
    /// </summary>
    private Dictionary<string, object> BuildMobileAttributes(ref Dictionary<string, object>? cached)
    {
        if (cached is null || _cachedMobileTestIdPrefix != TestIdPrefix)
        {
            _cachedMobileTestIdPrefix = TestIdPrefix;
            _mobilePanelTabsAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-testid"] = TestId("modeling-panel-tabs")
            };
            _previewTabAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-testid"] = TestId("modeling-panel-tab-preview")
            };
            _treeTabAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-testid"] = TestId("modeling-panel-tab-tree")
            };
            _inspectorTabAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-testid"] = TestId("modeling-panel-tab-inspector")
            };
            _issuesTabAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-testid"] = TestId("modeling-panel-tab-issues")
            };
            _changeViewSheetAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-testid"] = TestId("modeling-change-view-sheet")
            };
        }

        return cached;
    }

    /// <summary>The mobile panel tab strip is a TmTabs whose ids match <see cref="_activePanel"/>.</summary>
    private string MobilePanelTabId => MobilePanelName(_activePanel);

    private static string MobilePanelName(ModelingEditorPanel panel) => panel switch
    {
        ModelingEditorPanel.Tree => "tree",
        ModelingEditorPanel.Inspector => "inspector",
        ModelingEditorPanel.Issues => "issues",
        _ => "preview"
    };

    /// <summary>TmTabs selection receiver: stores the active panel the strip reports. Re-entering
    /// the Inspector tab with a PENDING edit arms a post-render focus move onto the name field —
    /// the compact band stacks the source card above the inspector, so the edited field would
    /// otherwise sit below the fold (review fix, phase 14).</summary>
    private Task SelectMobilePanelAsync(string tabId)
    {
        _activePanel = tabId switch
        {
            "tree" => ModelingEditorPanel.Tree,
            "inspector" => ModelingEditorPanel.Inspector,
            "issues" => ModelingEditorPanel.Issues,
            _ => ModelingEditorPanel.Preview
        };

        if (_activePanel == ModelingEditorPanel.Inspector && IsEditingEnabled && HasPendingModelChanges)
        {
            _focusInspectorFieldAfterRender = true;
        }

        return Task.CompletedTask;
    }

    /// <summary>Armed by the Inspector-tab re-entry with a pending edit: the next render focuses
    /// the inspector's name input (the primary edit field) through the editor-root hook.</summary>
    private bool _focusInspectorFieldAfterRender;

    /// <summary>The inspector edit-field selector the mobile re-entry focuses.</summary>
    internal const string InspectorNameFieldSelector = "#modeling-inspector-name";

    /// <summary>The Issues tab badge: the issue count as visible text; null (no badge span) at zero.</summary>
    private string? IssueTabBadge => IssueCount > 0
        ? IssueCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : null;

    /// <summary>The mobile Inspector tab badge: the localized "Stale" label while the loaded
    /// metadata is stale (final review m4 — the compact band has no Source tab to carry the
    /// docked stale marker); null (no badge) otherwise.</summary>
    private string? InspectorTabBadge => SourceIsStale ? Loc["TmModelingSourcePanel_Stale"] : null;

    // ── The compact header "⋯" overflow ──────────────────────────────────────

    /// <summary>
    /// The TmDropdown selection receiver of the compact header "⋯": every entry closes the menu
    /// first (TmDropdown's own contract) and then performs the editor-level action.
    /// </summary>
    private Task HandleMobileHeaderMenuAsync(string key) => key switch
    {
        "change-view" => OpenChangeViewAsync(),
        "generate" => GenerateDiagramFromLoadedModelAsync(),
        "open-editor" => OpenEditorFromMobileAsync(MobileHeaderTriggerSelector),
        _ => Task.CompletedTask
    };

    // ── The "Change view" sheet (F2/F3) ──────────────────────────────────────

    private Task OpenChangeViewAsync()
    {
        _changeViewOpen = true;
        return Task.CompletedTask;
    }

    private Task CloseChangeViewAsync()
    {
        _changeViewOpen = false;
        return Task.CompletedTask;
    }

    /// <summary>Opens the open-editor dialog with the CURRENT document (the mobile entries have
    /// no document argument — the dialog shows what the preview generated). The trigger that
    /// opened it is remembered, so the close returns focus there (the host-div proxy stays the
    /// fallback part of the selector list — Fáze 7 review).</summary>
    private Task OpenEditorFromMobileAsync(string returnTriggerSelector)
    {
        if (_document is null)
        {
            return Task.CompletedTask;
        }

        _openEditorFocusReturnSelector = returnTriggerSelector;
        return OpenGeneratedDiagramAsync(_document);
    }

    // ── The F5 action bar ────────────────────────────────────────────────────

    /// <summary>The cached action-bar item list and the inputs it was built from (final review:
    /// the five TmActionItem allocations plus their EventCallbacks are rebuilt only when one of
    /// the inputs changes — every list element is reference-stable between editor renders, so
    /// the TmMobileActionBar parameter diff stays quiet).</summary>
    private IReadOnlyList<TmActionItem>? _cachedMobileActionItems;
    private (bool WorkspaceMounted, ModelingModelDto? Model, ModelingEditorState State, DiagramDocument? Document, string? Prefix, string Culture, bool Pending) _mobileActionItemsKey;

    /// <summary>
    /// The mobile action bar items. With the workspace mounted: Reload / Change view / Issues on
    /// the bar and Generate diagram + Open in editor behind "More" (MaxVisible=3). Deliberately
    /// NO "Settings" item — the mockup's [E] entry is not part of the contract. Fáze 7 review
    /// (blocker): the empty / error / loading-without-model screens keep the bar reachable with
    /// the RECOVER entries only (Reload + Change view) — Generate/Open need a model and would be
    /// dead tiles there. Generate carries its own icon (the wand), so it is never confused with
    /// the adjacent Reload refresh glyph.
    /// </summary>
    private IReadOnlyList<TmActionItem> MobileActionItems
    {
        get
        {
            var key = (WorkspaceMounted, _model, _state, _document, TestIdPrefix, CultureInfo.CurrentUICulture.Name, IsEditingEnabled && HasPendingModelChanges);
            if (_cachedMobileActionItems is not null && _mobileActionItemsKey.Equals(key))
            {
                return _cachedMobileActionItems;
            }

            _mobileActionItemsKey = key;
            _cachedMobileActionItems = BuildMobileActionItems();
            return _cachedMobileActionItems;
        }
    }

    private IReadOnlyList<TmActionItem> BuildMobileActionItems()
    {
        var reload = new TmActionItem
        {
            Id = "reload",
            Label = Loc["TmModelingEditor_Reload"],
            Icon = IconNames.Refresh,
            OnClick = EventCallback.Factory.Create(this, ReloadModelAsync)
        };
        var changeView = new TmActionItem
        {
            Id = "change-view",
            Label = Loc["TmModelingEditor_ChangeView"],
            Icon = IconNames.Sliders,
            OnClick = EventCallback.Factory.Create(this, OpenChangeViewAsync)
        };

        if (!WorkspaceMounted)
        {
            return [reload, changeView];
        }

        // Fáze 14 (G2 light edits): while edits are pending the sticky Save/Discard entries LEAD
        // the bar (the user must not scroll the "More" overflow to save); they disappear again
        // once the pending set is empty.
        var pendingItems = IsEditingEnabled && HasPendingModelChanges
            ? new List<TmActionItem>
            {
                new()
                {
                    Id = "save",
                    Label = IsSaving ? Loc["TmModelingEditor_Saving"] : Loc["TmModelingEditor_Save"],
                    Icon = IconNames.Save,
                    TestId = TestId("modeling-action-save"),
                    Disabled = IsSaving,
                    OnClick = EventCallback.Factory.Create(this, SaveFromInspectorAsync)
                },
                new()
                {
                    Id = "discard",
                    Label = Loc["TmModelingEditor_Discard"],
                    Icon = IconNames.Trash2,
                    TestId = TestId("modeling-action-discard"),
                    OnClick = EventCallback.Factory.Create(this, DiscardFromInspectorAsync)
                }
            }
            : [];

        return
        [
            .. pendingItems,
            reload,
            changeView,
            new()
            {
                Id = "issues",
                Label = Loc["TmModelingEditor_Issues"],
                Icon = IconNames.AlertCircle,
                OnClick = EventCallback.Factory.Create(this, () => ActivatePanelAsync(ModelingEditorPanel.Issues))
            },
            new()
            {
                Id = "generate",
                Label = Loc["TmModelingDiagramPreview_Generate"],
                Icon = IconNames.WandSparkles,
                TestId = TestId("modeling-action-generate"),
                Disabled = _model is null || _state != ModelingEditorState.Loaded,
                OnClick = EventCallback.Factory.Create(this, GenerateDiagramFromLoadedModelAsync)
            },
            new()
            {
                Id = "open-editor",
                Label = Loc["TmModelingDiagramPreview_OpenInEditor"],
                Icon = IconNames.ExternalLink,
                TestId = TestId("modeling-action-open-editor"),
                Disabled = _document is null,
                OnClick = EventCallback.Factory.Create(this, () => OpenEditorFromMobileAsync(MobileActionBarTriggerSelector))
            }
        ];
    }

    // ── Status card (the mobile replacement of the status strip) ─────────────

    /// <summary>
    /// The four tiles of the mobile status card with their SHORT visible labels (final review
    /// M3: the label is visible text above the count — the long strip labels would wrap the
    /// 320px card) and the aria-hidden type icon each tile carries. Nodes/Edges reuse the short
    /// count labels; Elements/Relationships get the dedicated short keys.
    /// </summary>
    private (string Label, int Count, string Icon)[] StatusTiles =>
    [
        (Loc["TmModelingEditor_ElementsShort"], _model?.Elements.Count ?? 0, IconNames.Box),
        (Loc["TmModelingEditor_RelationshipsShort"], _model?.Relationships.Count ?? 0, IconNames.Link2),
        (Loc["TmModelingEditor_Nodes"], _document?.Nodes.Count ?? 0, IconNames.Circle),
        (Loc["TmModelingEditor_Edges"], _document?.Edges.Count ?? 0, IconNames.ArrowRight)
    ];
}
