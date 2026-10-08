using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Provides <see cref="WireframeComponentSchema"/> entries for all built-in Tempo.Blazor
/// components. This is the single source of truth for component metadata (types, categories,
/// default dimensions, and property definitions).
/// <para>
/// In the main <c>Tempo.Blazor</c> package, the built-in stencil pack provider reads
/// props directly from this class so the two never diverge.
/// </para>
/// <para>
/// API / MCP projects that reference only <c>Tempo.Blazor.Abstractions</c> can use this
/// class directly — no Blazor rendering dependency.
/// </para>
/// </summary>
public sealed class BuiltInComponentSchemas : IWireframeSchemaSource
{
    /// <inheritdoc/>
    public string SourceId => "BuiltIn";

    /// <inheritdoc/>
    public int Priority => 0;

    // ── Size preset dictionaries (shared across button-family) ────────────────

    /// <summary>Size presets for standard buttons.</summary>
    public static readonly IReadOnlyDictionary<string, (double W, double H)> ButtonSizes =
        new Dictionary<string, (double, double)>
        {
            ["xs"] = (80,  24), ["sm"] = (100, 30), ["md"] = (120, 36), ["lg"] = (140, 44),
        };

    /// <summary>Size presets for split buttons.</summary>
    public static readonly IReadOnlyDictionary<string, (double W, double H)> SplitButtonSizes =
        new Dictionary<string, (double, double)>
        {
            ["xs"] = (100, 24), ["sm"] = (120, 30), ["md"] = (140, 36), ["lg"] = (160, 44),
        };

    /// <summary>Size presets for icon-only buttons (square).</summary>
    public static readonly IReadOnlyDictionary<string, (double W, double H)> IconButtonSizes =
        new Dictionary<string, (double, double)>
        {
            ["xs"] = (24, 24), ["sm"] = (28, 28), ["md"] = (36, 36), ["lg"] = (44, 44),
        };

    /// <summary>Size presets for badges.</summary>
    public static readonly IReadOnlyDictionary<string, (double W, double H)> BadgeSizes =
        new Dictionary<string, (double, double)>
        {
            ["sm"] = (48, 18), ["md"] = (60, 22), ["lg"] = (72, 26),
        };

    /// <summary>Size presets for spinners.</summary>
    public static readonly IReadOnlyDictionary<string, (double W, double H)> SpinnerSizes =
        new Dictionary<string, (double, double)>
        {
            ["sm"] = (16, 16), ["md"] = (32, 32), ["lg"] = (48, 48),
        };

    // ── Prop helper ───────────────────────────────────────────────────────────

    private static PropDef P(string name, string display, PropType type,
        object? def = null, string[]? opts = null, string? cat = null, bool req = false)
        => new() { Name = name, DisplayName = display, Type = type,
                   Default = def, Options = opts, Category = cat, IsRequired = req };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> BuiltInRoles =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["TmChart"] = ["chart"],
            ["TmSankeyChart"] = ["chart"],
            ["TmCalendarHeatmap"] = ["chart"],
            ["TmSparkline"] = ["sparkline"],
            ["TmGauge"] = ["gauge"],
            ["TmStockChart"] = ["chart"],
            ["TmKanbanBoard"] = ["kanban-board"],
            ["TmPivotTable"] = ["data-table", "chart"],
            ["TmGantt"] = ["scheduler", "timeline"],
            ["TmWorkflowDesignerCanvas"] = ["diagram-editor"],
            ["TmDiagramEditor"] = ["diagram-editor"],
            ["TmSpreadsheet"] = ["spreadsheet"],
            ["TmDocumentEditor"] = ["rich-text-editor"],
            ["TmNotionEditor"] = ["rich-text-editor"],
            ["TmChat"] = ["text-input", "list-view"],
            ["TmWorkflowToolbox"] = ["toolbar"],
            ["TmWorkflowPropertiesPanel"] = ["form-field"],
            ["TmWorkflowMinimap"] = ["diagram-editor"],
            ["TmTimeline"] = ["timeline"],
            ["TmStepper"] = ["stepper"],
            ["TmScheduler"] = ["scheduler"],
            ["TmDashboard"] = ["dashboard"],
            ["TmMarkdownEditor"] = ["markdown-editor"],
            ["TmCodeEditor"] = ["code-editor"],
            ["TmRichEditorFull"] = ["rich-text-editor"],
            ["TmRichEditorSimple"] = ["rich-text-editor"],
            ["TmImageGallery"] = ["image-gallery"],
            ["TmImportWizard"] = ["stepper", "file-drop"],
            ["TmFilterBuilder"] = ["filter-builder"],
            ["TmActivityLog"] = ["timeline", "list-view"],
            ["TmActivityComments"] = ["list-view", "text-area"],
            ["TmActivityAttachments"] = ["attachment-manager"],
            ["TmActivityTimeline"] = ["timeline"],
            ["TmLightbox"] = ["image-gallery"],
            ["TmExportOptions"] = ["form-field", "button"],
            ["TmImportPreview"] = ["data-table"],
            ["TmTreeView"] = ["list-view"],
            ["TmPdfViewer"] = ["pdf-viewer"],
            ["TmPdfAnnotator"] = ["pdf-annotator"],
            ["TmMap"] = ["map"],
            ["TmCommentComposer"] = ["text-area", "avatar"],
            ["TmCommentReactions"] = ["chip"],
            ["TmReactionPicker"] = ["chip", "popover"],
            ["TmShareLinkPanel"] = ["copy-button", "link"],
            ["TmSubmissionStatusTimeline"] = ["timeline", "stepper"],
            ["TmAuditTrailViewer"] = ["timeline", "list-view"],
            ["TmAuditLogViewer"] = ["audit-log"],
            ["TmDeadlineCalculator"] = ["deadline-calculator"],
            ["TmLedgerGrid"] = ["ledger-grid"],
            ["TmMoneyDisplay"] = ["money-display"],
            ["TmKycWizard"] = ["kyc-wizard"],
            ["TmScreeningResultPanel"] = ["screening-result-panel"],
            ["TmDataImport"] = ["data-import"],
            ["TmRedactionLayer"] = ["redaction-layer"],
            ["TmAIPrompt"] = ["text-area", "button"],
            ["TmWidgetSelector"] = ["dashboard", "card"],
            ["__group__"] = ["section"],
            ["TmGanttPortfolio"] = ["scheduler", "dashboard"],
            ["TmTreeList"] = ["list-view", "data-table"],
            ["TmNotionPage"] = ["rich-text-editor"],
            ["TmModelingEditor"] = ["diagram-editor"],
            ["TmFileManager"] = ["attachment-manager", "file-drop"],
            ["TmDocumentManager"] = ["attachment-manager", "list-view"],
            ["TmDivider"] = ["section"],
            ["TmText"] = ["text"],
            ["TmCard"] = ["card"],
            ["TmStatCard"] = ["stat-card"],
            ["TmBadge"] = ["badge"],
            ["TmChip"] = ["chip"],
            ["TmChipGroup"] = ["chip"],
            ["TmFilterChip"] = ["chip", "filter-builder"],
            ["TmAccordion"] = ["accordion"],
            ["TmAccordionItem"] = ["accordion"],
            ["TmEmptyState"] = ["empty-state"],
            ["TmQRCode"] = ["icon"],
            ["TmBarcode"] = ["icon"],
            ["TmChangeDiff"] = ["diff-view"],
            ["TmMultiViewList"] = ["list-view"],
            ["TmDataTable"] = ["data-table"],
            ["TmPagination"] = ["pagination"],
            ["TmBulkActionBar"] = ["bulk-action-bar"],
            ["TmColumnFilter"] = ["column-filter"],
            ["TmColumnPicker"] = ["column-picker"],
            ["TmViewManager"] = ["view-manager"],
            ["TmTabs"] = ["tabs"],
            ["TmBreadcrumbs"] = ["breadcrumbs"],
            ["TmTabPanel"] = ["tabs"],
            ["TmContextMenu"] = ["context-menu"],
            ["TmContextMenuItem"] = ["context-menu"],
            ["TmBottomNavigation"] = ["bottom-navigation"],
            ["TmMobileActionBar"] = ["action-bar"],
            ["TmNavigationGuard"] = ["navigation-guard"],
            ["TmScrollSpyNav"] = ["scroll-spy-nav"],
            ["TmMenu"] = ["menu"],
            ["TmTopBar"] = ["navigation-bar"],
            ["TmSidebar"] = ["sidebar"],
            ["TmDrawer"] = ["drawer"],
            ["TmSection"] = ["section"],
            ["TmCommandPalette"] = ["command-palette", "search-input"],
            ["TmKeyboardShortcutsHelp"] = ["keyboard-shortcuts"],
            ["TmStackLayout"] = ["section"],
            ["TmLayoutObserver"] = ["layout-observer"],
            ["TmSplitter"] = ["section"],
            ["TmDockManager"] = ["dashboard", "section"],
            ["TmToolbar"] = ["toolbar"],
            ["TmToolbarButton"] = ["toolbar", "button"],
            ["TmToolbarDivider"] = ["toolbar"],
            ["TmFormActionBar"] = ["toolbar"],
            ["TmAlert"] = ["alert"],
            ["TmModal"] = ["modal"],
            ["TmFocusScope"] = ["focus-scope"],
            ["TmSheetHandle"] = ["sheet-handle"],
            ["TmDialog"] = ["dialog"],
            ["TmTooltip"] = ["tooltip"],
            ["TmPopover"] = ["popover"],
            ["TmProgressBar"] = ["progress-bar"],
            ["TmSpinner"] = ["spinner"],
            ["TmSkeleton"] = ["skeleton"],
            ["TmToastContainer"] = ["toast-container"],
            ["TmAutoSaveIndicator"] = ["badge"],
            ["TmNotificationBell"] = ["notification-bell"],
            ["TmFormSection"] = ["form-section"],
            ["TmFormRow"] = ["form-section", "form-field"],
            ["TmFormField"] = ["form-field"],
            ["TmInlineEdit"] = ["inline-edit"],
            ["TmValidatedField"] = ["form-field"],
            ["TmFormValidationMessage"] = ["validation-message"],
            ["TmValidationSummary"] = ["validation-summary"],
            ["TmDynamicFormRenderer"] = ["dynamic-form"],
            ["TmConditionBuilder"] = ["filter-builder"],
            ["TmFormulaBuilder"] = ["expression-editor"],
            ["TmFileDropZone"] = ["file-drop"],
            ["TmAttachmentManager"] = ["attachment-manager"],
            ["TmAvatar"] = ["avatar"],
            ["TmAvatarGroup"] = ["avatar-group"],
            ["TmIcon"] = ["icon"],
            ["TmColorPicker"] = ["select"],
            ["TmFlatColorPicker"] = ["select"],
            ["TmColorPalette"] = ["select"],
            ["TmColorGradient"] = ["select"],
            ["TmButton"] = ["button"],
            ["TmSplitButton"] = ["split-button"],
            ["TmCopyButton"] = ["copy-button"],
            ["TmFloatingActionButton"] = ["floating-action-button"],
            ["TmTextInput"] = ["text-input"],
            ["TmTextArea"] = ["text-area"],
            ["TmNumberInput"] = ["number-input"],
            ["TmSearchInput"] = ["search-input"],
            ["TmQueryInput"] = ["text-input", "search-input"],
            ["TmCurrencyInput"] = ["currency-input"],
            ["TmDecimalInput"] = ["decimal-input", "number-input"],
            ["TmCheckbox"] = ["checkbox"],
            ["TmRadio"] = ["radio"],
            ["TmRadioGroup"] = ["radio-group"],
            ["TmToggle"] = ["toggle"],
            ["TmToggleSection"] = ["toggle", "section"],
            ["TmSelect"] = ["select"],
            ["TmMultiSelect"] = ["multi-select"],
            ["TmCascadingSelect"] = ["cascading-select"],
            ["TmFilterableDropdown"] = ["dropdown", "search-input"],
            ["TmEntityPicker"] = ["entity-picker"],
            ["TmUserPicker"] = ["entity-picker"],
            ["TmExpressionEditor"] = ["expression-editor"],
            ["TmPasswordStrengthIndicator"] = ["password-strength"],
            ["TmSlider"] = ["slider"],
            ["TmRangeSlider"] = ["range-slider"],
            ["TmRating"] = ["rating"],
            ["TmMaskedTextBox"] = ["masked-input", "otp-input"],
            ["TmMultiColumnComboBox"] = ["combo-box", "data-table"],
            ["TmSignature"] = ["signature-pad"],
            ["TmSignatureCapture"] = ["signature-pad"],
            ["TmTagPicker"] = ["tag-picker"],
            ["TmDatePicker"] = ["date-picker"],
            ["TmDateTimePicker"] = ["datetime-picker"],
            ["TmTimePicker"] = ["time-picker"],
            ["TmDateRangePicker"] = ["date-range-picker"],
            ["TmTimeRangePicker"] = ["date-range-picker", "time-picker"],
            ["TmDateTimeRangePicker"] = ["date-range-picker", "datetime-picker"],
            ["TmTimeInput"] = ["time-picker"],
            ["TmCalendarView"] = ["calendar-view"],
            ["TmCalendarGrid"] = ["calendar-view"],
            ["TmRecurrenceEditor"] = ["recurrence-editor"],
            ["TmDropdown"] = ["dropdown"],
            ["TmDropdownItem"] = ["dropdown"],
        };

    private static readonly HashSet<string> BuiltInContainers =
        new(StringComparer.Ordinal)
        {
            "__group__",
            "TmCard",
            "TmDrawer",
            "TmSection",
            "TmStackLayout",
            "TmModal",
            "TmFocusScope",
            "TmDialog",
            "TmFormSection",
            "TmFormRow"
        };

    private static WireframeComponentSchema WithBuiltInMetadata(WireframeComponentSchema schema)
    {
        var hasRoles = BuiltInRoles.TryGetValue(schema.Type, out var roles);
        var isContainer = schema.IsContainer || BuiltInContainers.Contains(schema.Type);
        if (!hasRoles && isContainer == schema.IsContainer)
            return schema;

        return new WireframeComponentSchema
        {
            Type = schema.Type,
            ScopeAppId = schema.ScopeAppId,
            LocalType = schema.LocalType,
            Category = schema.Category,
            DisplayName = schema.DisplayName,
            Roles = hasRoles ? schema.Roles ?? roles : schema.Roles,
            IsBuiltIn = schema.IsBuiltIn,
            IsContainer = isContainer,
            DefaultWidth = schema.DefaultWidth,
            DefaultHeight = schema.DefaultHeight,
            Props = schema.Props,
            SizePresets = schema.SizePresets,
        };
    }

    // ── Schema definitions ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IEnumerable<WireframeComponentSchema> GetSchemas()
    {
        foreach (var s in Buttons())       yield return WithBuiltInMetadata(s);
        foreach (var s in Avatars())       yield return WithBuiltInMetadata(s);
        foreach (var s in Icons())         yield return WithBuiltInMetadata(s);
        foreach (var s in Inputs())        yield return WithBuiltInMetadata(s);
        foreach (var s in Tags())          yield return WithBuiltInMetadata(s);
        foreach (var s in Pickers())       yield return WithBuiltInMetadata(s);
        foreach (var s in Dropdowns())     yield return WithBuiltInMetadata(s);
        foreach (var s in DataDisplay())   yield return WithBuiltInMetadata(s);
        foreach (var s in DataTable())     yield return WithBuiltInMetadata(s);
        foreach (var s in Feedback())      yield return WithBuiltInMetadata(s);
        foreach (var s in Notifications()) yield return WithBuiltInMetadata(s);
        foreach (var s in Navigation())    yield return WithBuiltInMetadata(s);
        foreach (var s in Layout())        yield return WithBuiltInMetadata(s);
        foreach (var s in Toolbar())       yield return WithBuiltInMetadata(s);
        foreach (var s in Forms())         yield return WithBuiltInMetadata(s);
        foreach (var s in Files())         yield return WithBuiltInMetadata(s);
        foreach (var s in Charts())        yield return WithBuiltInMetadata(s);
        foreach (var s in Workflow())      yield return WithBuiltInMetadata(s);
        foreach (var s in Complex())       yield return WithBuiltInMetadata(s);
        foreach (var s in Color())         yield return WithBuiltInMetadata(s);
        foreach (var s in EditorsAndApps()) yield return WithBuiltInMetadata(s);
    }

    // ── BUTTONS ───────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Buttons()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmButton", Category = "Buttons", DisplayName = "Button",
            DefaultWidth = 120, DefaultHeight = 36, SizePresets = ButtonSizes,
            Props =
            [
                P("label",    "Label",    PropType.String, "Button",    cat: "Content",    req: true),
                P("variant",  "Variant",  PropType.Enum,   "primary",   cat: "Appearance",
                    opts: ["primary","secondary","ghost","danger","outline","link","default"]),
                P("size",     "Size",     PropType.Enum,   "md",        cat: "Appearance",
                    opts: ["xs","sm","md","lg"]),
                P("icon",     "Icon",     PropType.Icon,   cat: "Content"),
                P("disabled", "Disabled", PropType.Bool,   false,       cat: "Behavior"),
                P("loading",  "Loading",  PropType.Bool,   false,       cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSplitButton", Category = "Buttons", DisplayName = "Split Button",
            DefaultWidth = 140, DefaultHeight = 36, SizePresets = SplitButtonSizes,
            Props =
            [
                P("label",   "Label",   PropType.String, "Action",  cat: "Content"),
                P("variant", "Variant", PropType.Enum,   "primary", cat: "Appearance",
                    opts: ["primary","secondary","ghost","danger"]),
                P("size",    "Size",    PropType.Enum,   "md",      cat: "Appearance",
                    opts: ["xs","sm","md","lg"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCopyButton", Category = "Buttons", DisplayName = "Copy Button",
            DefaultWidth = 36, DefaultHeight = 36, SizePresets = IconButtonSizes,
            Props =
            [
                P("size", "Size", PropType.Enum, "md", cat: "Appearance", opts: ["xs","sm","md","lg"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFloatingActionButton", Category = "Buttons", DisplayName = "Floating Action Button",
            DefaultWidth = 56, DefaultHeight = 56,
            Props =
            [
                P("icon",    "Icon",    PropType.Icon,   "plus",   cat: "Content"),
                P("variant", "Variant", PropType.Enum,   "primary",cat: "Appearance",
                    opts: ["primary","secondary","danger"]),
                P("size",    "Size",    PropType.Enum,   "md",     cat: "Appearance",
                    opts: ["sm","md","lg"]),
            ]
        };
    }

    // ── AVATARS ───────────────────────────────────────────────────────────────

    /// <summary>Size presets for avatars.</summary>
    public static readonly IReadOnlyDictionary<string, (double W, double H)> AvatarSizes =
        new Dictionary<string, (double, double)>
        {
            ["xs"] = (24, 24), ["sm"] = (32, 32), ["md"] = (40, 40),
            ["lg"] = (48, 48), ["xl"] = (56, 56), ["xxl"] = (64, 64),
        };

    private static IEnumerable<WireframeComponentSchema> Avatars()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmAvatar", Category = "Avatars", DisplayName = "Avatar",
            DefaultWidth = 40, DefaultHeight = 40, SizePresets = AvatarSizes,
            Props =
            [
                P("name",  "Name",  PropType.String, "AB",     cat: "Content"),
                P("size",  "Size",  PropType.Enum,   "md",     cat: "Appearance",
                    opts: ["xs","sm","md","lg","xl","xxl"]),
                P("shape", "Shape", PropType.Enum,   "circle", cat: "Appearance",
                    opts: ["circle","square"]),
                P("color", "Color", PropType.Enum,   "gray",   cat: "Appearance",
                    opts: ["gray","blue","green","purple","red","yellow"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmAvatarGroup", Category = "Avatars", DisplayName = "Avatar Group",
            DefaultWidth = 120, DefaultHeight = 40,
            Props =
            [
                P("count", "Count", PropType.Int,  3,    cat: "State"),
                P("max",   "Max",   PropType.Int,  3,    cat: "Appearance"),
                P("size",  "Size",  PropType.Enum, "md", cat: "Appearance",
                    opts: ["xs","sm","md","lg","xl","xxl"]),
            ]
        };
    }

    // ── ICONS ─────────────────────────────────────────────────────────────────

    /// <summary>Size presets for icons.</summary>
    public static readonly IReadOnlyDictionary<string, (double W, double H)> IconSizes =
        new Dictionary<string, (double, double)>
        {
            ["sm"] = (16, 16), ["md"] = (24, 24), ["lg"] = (32, 32), ["xl"] = (48, 48),
        };

    private static IEnumerable<WireframeComponentSchema> Icons()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmIcon", Category = "Icons", DisplayName = "Icon",
            DefaultWidth = 24, DefaultHeight = 24, SizePresets = IconSizes,
            Props =
            [
                P("name",  "Name",       PropType.String, "circle", cat: "Content"),
                P("svg",   "Custom SVG", PropType.String, "",       cat: "Content"),
                P("size",  "Size",       PropType.Enum,   "md",     cat: "Appearance",
                    opts: ["sm","md","lg","xl"]),
                P("color", "Color",      PropType.Enum,   "gray",   cat: "Appearance",
                    opts: ["gray","blue","green","red","yellow","purple"]),
                P("style", "Style",      PropType.String, "",       cat: "Appearance"),
            ]
        };
    }

    // ── INPUTS ────────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Inputs()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmTextInput", Category = "Inputs", DisplayName = "Text Input",
            DefaultWidth = 240, DefaultHeight = 56,
            Props =
            [
                P("label",       "Label",       PropType.String, "Label",          cat: "Content"),
                P("placeholder", "Placeholder", PropType.String, "Enter text...",  cat: "Content"),
                P("type",        "Type",        PropType.Enum,   "text",           cat: "Behavior",
                    opts: ["text","password","email","tel","url"]),
                P("maxLength",   "Max Length",  PropType.Int,    0,                cat: "Behavior"),
                P("required",    "Required",    PropType.Bool,   false,            cat: "Behavior"),
                P("disabled",    "Disabled",    PropType.Bool,   false,            cat: "Behavior"),
                P("readOnly",    "Read Only",   PropType.Bool,   false,            cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmQueryInput", Category = "Inputs", DisplayName = "Query Input",
            DefaultWidth = 320, DefaultHeight = 56,
            Props =
            [
                P("label",       "Label",       PropType.String, "",                cat: "Content"),
                P("placeholder", "Placeholder", PropType.String, "Type a query...", cat: "Content"),
                P("monospace",   "Monospace",   PropType.Bool,   true,              cat: "Behavior"),
                P("disabled",    "Disabled",    PropType.Bool,   false,             cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmTextArea", Category = "Inputs", DisplayName = "Text Area",
            DefaultWidth = 240, DefaultHeight = 100,
            Props =
            [
                P("label",       "Label",       PropType.String, "Label",         cat: "Content"),
                P("placeholder", "Placeholder", PropType.String, "Enter text...", cat: "Content"),
                P("rows",        "Rows",        PropType.Int,    3,               cat: "Appearance"),
                P("required",    "Required",    PropType.Bool,   false,           cat: "Behavior"),
                P("disabled",    "Disabled",    PropType.Bool,   false,           cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmNumberInput", Category = "Inputs", DisplayName = "Number Input",
            DefaultWidth = 200, DefaultHeight = 56,
            Props =
            [
                P("label",    "Label",    PropType.String, "Label", cat: "Content"),
                P("min",      "Min",      PropType.Double, cat: "Behavior"),
                P("max",      "Max",      PropType.Double, cat: "Behavior"),
                P("step",     "Step",     PropType.Double, 1.0,     cat: "Behavior"),
                P("required", "Required", PropType.Bool,   false,   cat: "Behavior"),
                P("disabled", "Disabled", PropType.Bool,   false,   cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSearchInput", Category = "Inputs", DisplayName = "Search Input",
            DefaultWidth = 240, DefaultHeight = 36,
            Props =
            [
                P("placeholder", "Placeholder", PropType.String, "Search...", cat: "Content"),
                P("disabled",    "Disabled",    PropType.Bool,   false,       cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCurrencyInput", Category = "Inputs", DisplayName = "Currency Input",
            DefaultWidth = 200, DefaultHeight = 56,
            Props =
            [
                P("label",          "Label",           PropType.String, "Amount", cat: "Content"),
                P("currencySymbol", "Currency Symbol", PropType.String, "Kč",     cat: "Appearance"),
                P("required",       "Required",        PropType.Bool,   false,    cat: "Behavior"),
                P("disabled",       "Disabled",        PropType.Bool,   false,    cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDecimalInput", Category = "Inputs", DisplayName = "Decimal Input",
            DefaultWidth = 200, DefaultHeight = 56,
            Props =
            [
                P("label",       "Label",       PropType.String, "Amount", cat: "Content"),
                P("suffix",      "Suffix",      PropType.String,           cat: "Appearance"),
                P("percent",     "Percent",     PropType.Bool,   false,    cat: "Behavior"),
                P("min",         "Min",         PropType.Double,           cat: "Behavior"),
                P("max",         "Max",         PropType.Double,           cat: "Behavior"),
                P("step",        "Step",        PropType.Double, 1.0,      cat: "Behavior"),
                P("required",    "Required",    PropType.Bool,   false,    cat: "Behavior"),
                P("disabled",    "Disabled",    PropType.Bool,   false,    cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCheckbox", Category = "Inputs", DisplayName = "Checkbox",
            DefaultWidth = 140, DefaultHeight = 20,
            Props =
            [
                P("label",         "Label",         PropType.String, "Checkbox", cat: "Content"),
                P("checked",       "Checked",       PropType.Bool,   false,      cat: "State"),
                P("indeterminate", "Indeterminate", PropType.Bool,   false,      cat: "State"),
                P("required",      "Required",      PropType.Bool,   false,      cat: "Behavior"),
                P("disabled",      "Disabled",      PropType.Bool,   false,      cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmRadio", Category = "Inputs", DisplayName = "Radio",
            DefaultWidth = 140, DefaultHeight = 20,
            Props =
            [
                P("label",    "Label",    PropType.String, "Option", cat: "Content"),
                P("checked",  "Checked",  PropType.Bool,   false,    cat: "State"),
                P("disabled", "Disabled", PropType.Bool,   false,    cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmRadioGroup", Category = "Inputs", DisplayName = "Radio Group",
            DefaultWidth = 200, DefaultHeight = 80,
            Props =
            [
                P("label",    "Label",    PropType.String,     "Options", cat: "Content"),
                P("options",  "Options",  PropType.StringList, cat: "Content"),
                P("required", "Required", PropType.Bool,       false,     cat: "Behavior"),
                P("disabled", "Disabled", PropType.Bool,       false,     cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmToggle", Category = "Inputs", DisplayName = "Toggle",
            DefaultWidth = 100, DefaultHeight = 20,
            Props =
            [
                P("label",    "Label",    PropType.String, "Toggle", cat: "Content"),
                P("checked",  "Checked",  PropType.Bool,   false,    cat: "State"),
                P("disabled", "Disabled", PropType.Bool,   false,    cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmToggleSection", Category = "Inputs", DisplayName = "Toggle Section",
            DefaultWidth = 240, DefaultHeight = 40,
            Props =
            [
                P("label",    "Label",    PropType.String, "Section", cat: "Content"),
                P("expanded", "Expanded", PropType.Bool,   true,      cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSelect", Category = "Inputs", DisplayName = "Select",
            DefaultWidth = 200, DefaultHeight = 56,
            Props =
            [
                P("label",       "Label",       PropType.String, "Label",            cat: "Content"),
                P("placeholder", "Placeholder", PropType.String, "Select option...", cat: "Content"),
                P("multiple",    "Multiple",    PropType.Bool,   false,              cat: "Behavior"),
                P("clearable",   "Clearable",   PropType.Bool,   false,              cat: "Behavior"),
                P("required",    "Required",    PropType.Bool,   false,              cat: "Behavior"),
                P("disabled",    "Disabled",    PropType.Bool,   false,              cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmMultiSelect", Category = "Inputs", DisplayName = "Multi Select",
            DefaultWidth = 240, DefaultHeight = 56,
            Props =
            [
                P("label",       "Label",       PropType.String, "Label",          cat: "Content"),
                P("placeholder", "Placeholder", PropType.String, "Select items...", cat: "Content"),
                P("required",    "Required",    PropType.Bool,   false,            cat: "Behavior"),
                P("disabled",    "Disabled",    PropType.Bool,   false,            cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCascadingSelect", Category = "Inputs", DisplayName = "Cascading Select",
            DefaultWidth = 300, DefaultHeight = 56,
            Props =
            [
                P("label",    "Label",    PropType.String, "Label", cat: "Content"),
                P("levels",   "Levels",   PropType.Int,    2,       cat: "Appearance"),
                P("disabled", "Disabled", PropType.Bool,   false,   cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFilterableDropdown", Category = "Inputs", DisplayName = "Filterable Dropdown",
            DefaultWidth = 200, DefaultHeight = 56,
            Props =
            [
                P("label",       "Label",       PropType.String, "Label",           cat: "Content"),
                P("placeholder", "Placeholder", PropType.String, "Filter...",       cat: "Content"),
                P("disabled",    "Disabled",    PropType.Bool,   false,             cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmEntityPicker", Category = "Inputs", DisplayName = "Entity Picker",
            DefaultWidth = 240, DefaultHeight = 56,
            Props =
            [
                P("label",       "Label",       PropType.String, "Select entity", cat: "Content"),
                P("placeholder", "Placeholder", PropType.String, "Choose...",     cat: "Content"),
                P("multiple",    "Multiple",    PropType.Bool,   false,           cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmUserPicker", Category = "Inputs", DisplayName = "User Picker",
            DefaultWidth = 240, DefaultHeight = 56,
            Props =
            [
                P("label",       "Label",       PropType.String, "Owner",      cat: "Content"),
                P("placeholder", "Placeholder", PropType.String, "Search...",  cat: "Content"),
                P("disabled",    "Disabled",    PropType.Bool,   false,        cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmExpressionEditor", Category = "Inputs", DisplayName = "Expression Editor",
            DefaultWidth = 280, DefaultHeight = 56,
            Props =
            [
                P("label",       "Label",       PropType.String, "Expression",   cat: "Content"),
                P("placeholder", "Placeholder", PropType.String, "{expression}", cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmPasswordStrengthIndicator", Category = "Inputs", DisplayName = "Password Strength",
            DefaultWidth = 240, DefaultHeight = 48,
            Props =
            [
                P("strength", "Strength", PropType.Int, 3, cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSlider", Category = "Inputs", DisplayName = "Slider",
            DefaultWidth = 180, DefaultHeight = 32,
            Props =
            [
                P("label",    "Label",    PropType.String, "",    cat: "Content"),
                P("min",      "Min",      PropType.Double, 0.0,   cat: "Behavior"),
                P("max",      "Max",      PropType.Double, 100.0, cat: "Behavior"),
                P("value",    "Value",    PropType.Double, 40.0,  cat: "State"),
                P("showValue","Show Value",PropType.Bool,  true,  cat: "Appearance"),
                P("disabled", "Disabled", PropType.Bool,   false, cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmRangeSlider", Category = "Inputs", DisplayName = "Range Slider",
            DefaultWidth = 180, DefaultHeight = 32,
            Props =
            [
                P("label",  "Label",  PropType.String, "",    cat: "Content"),
                P("min",    "Min",    PropType.Double, 0.0,   cat: "Behavior"),
                P("max",    "Max",    PropType.Double, 100.0, cat: "Behavior"),
                P("from",   "From",   PropType.Double, 20.0,  cat: "State"),
                P("to",     "To",     PropType.Double, 70.0,  cat: "State"),
                P("disabled","Disabled",PropType.Bool, false, cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmRating", Category = "Inputs", DisplayName = "Rating",
            DefaultWidth = 120, DefaultHeight = 24,
            Props =
            [
                P("value",   "Value",  PropType.Int,  3,     cat: "State"),
                P("max",     "Max",    PropType.Int,  5,     cat: "Appearance"),
                P("disabled","Disabled",PropType.Bool,false, cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmMaskedTextBox", Category = "Inputs", DisplayName = "Masked Text Box",
            DefaultWidth = 180, DefaultHeight = 36,
            Props =
            [
                P("label",    "Label",    PropType.String, "Label",       cat: "Content"),
                P("mask",     "Mask",     PropType.String, "__/__/____",  cat: "Behavior"),
                P("disabled", "Disabled", PropType.Bool,   false,         cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmMultiColumnComboBox", Category = "Inputs", DisplayName = "Multi-Column Combo",
            DefaultWidth = 200, DefaultHeight = 36,
            Props =
            [
                P("label",       "Label",       PropType.String, "Label",      cat: "Content"),
                P("placeholder", "Placeholder", PropType.String, "Select...",  cat: "Content"),
                P("columns",     "Columns",     PropType.Int,    2,            cat: "Appearance"),
                P("disabled",    "Disabled",    PropType.Bool,   false,        cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSignature", Category = "Inputs", DisplayName = "Signature",
            DefaultWidth = 240, DefaultHeight = 100,
            Props =
            [
                P("placeholder", "Placeholder", PropType.String, "Sign here", cat: "Content"),
                P("signed",      "Signed",       PropType.Bool,   false,       cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSignatureCapture", Category = "Inputs", DisplayName = "Signature Capture",
            DefaultWidth = 260, DefaultHeight = 140,
            Props =
            [
                P("placeholder", "Placeholder", PropType.String, "Draw your signature", cat: "Content"),
                P("signed",      "Signed",       PropType.Bool,   false,                cat: "State"),
            ]
        };
    }

    // ── TAGS ─────────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Tags()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmTagPicker", Category = "Tags", DisplayName = "Tag Picker",
            DefaultWidth = 240, DefaultHeight = 40,
            Props =
            [
                P("tags",        "Tags",         PropType.StringList, cat: "Content"),
                P("allowCreate", "Allow Create", PropType.Bool, false, cat: "Behavior"),
                P("disabled",    "Disabled",     PropType.Bool, false, cat: "Behavior"),
            ]
        };
    }

    // ── PICKERS ───────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Pickers()
    {
        var dateBehavior = new[] {
            P("format",   "Format",   PropType.String, "dd.mm.yyyy", cat: "Appearance"),
            P("min",      "Min",      PropType.String, "",           cat: "Behavior"),
            P("max",      "Max",      PropType.String, "",           cat: "Behavior"),
            P("required", "Required", PropType.Bool,   false,        cat: "Behavior"),
            P("disabled", "Disabled", PropType.Bool,   false,        cat: "Behavior"),
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDatePicker", Category = "Pickers", DisplayName = "Date Picker",
            DefaultWidth = 200, DefaultHeight = 56,
            Props = [P("label", "Label", PropType.String, "Date", cat: "Content"), ..dateBehavior]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDateTimePicker", Category = "Pickers", DisplayName = "Date & Time Picker",
            DefaultWidth = 200, DefaultHeight = 56,
            Props = [P("label", "Label", PropType.String, "Date & Time", cat: "Content"), ..dateBehavior]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmTimePicker", Category = "Pickers", DisplayName = "Time Picker",
            DefaultWidth = 160, DefaultHeight = 56,
            Props = [P("label", "Label", PropType.String, "Time", cat: "Content"), ..dateBehavior]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDateRangePicker", Category = "Pickers", DisplayName = "Date Range Picker",
            DefaultWidth = 320, DefaultHeight = 56,
            Props = [P("label", "Label", PropType.String, "Date range", cat: "Content"), ..dateBehavior]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmTimeRangePicker", Category = "Pickers", DisplayName = "Time Range Picker",
            DefaultWidth = 280, DefaultHeight = 56,
            Props =
            [
                P("label",    "Label",    PropType.String, "Time range", cat: "Content"),
                P("disabled", "Disabled", PropType.Bool,   false,        cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDateTimeRangePicker", Category = "Pickers", DisplayName = "DateTime Range Picker",
            DefaultWidth = 400, DefaultHeight = 56,
            Props =
            [
                P("label",    "Label",    PropType.String, "DateTime range", cat: "Content"),
                P("disabled", "Disabled", PropType.Bool,   false,            cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmTimeInput", Category = "Pickers", DisplayName = "Time Input",
            DefaultWidth = 120, DefaultHeight = 36,
            Props =
            [
                P("disabled", "Disabled", PropType.Bool, false, cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCalendarView", Category = "Pickers", DisplayName = "Calendar View",
            DefaultWidth = 280, DefaultHeight = 260,
            Props =
            [
                P("month",       "Month",        PropType.String, "January", cat: "Content"),
                P("year",        "Year",         PropType.Int,    2025,      cat: "Content"),
                P("selectedDay", "Selected Day", PropType.Int,    15,        cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCalendarGrid", Category = "Pickers", DisplayName = "Calendar Grid",
            DefaultWidth = 240, DefaultHeight = 200,
            Props =
            [
                P("month", "Month", PropType.String, "January", cat: "Content"),
                P("year",  "Year",  PropType.Int,    2025,      cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmRecurrenceEditor", Category = "Pickers", DisplayName = "Recurrence Editor",
            DefaultWidth = 280, DefaultHeight = 120,
            Props =
            [
                P("frequency", "Frequency", PropType.Enum, "weekly", cat: "State",
                    opts: ["daily","weekly","monthly","yearly"]),
                P("interval",  "Interval",  PropType.Int,  1,        cat: "State"),
            ]
        };
    }

    // ── DROPDOWNS ────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Dropdowns()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmDropdown", Category = "Dropdowns", DisplayName = "Dropdown",
            DefaultWidth = 160, DefaultHeight = 36,
            Props =
            [
                P("text",     "Text",     PropType.String, "Options", cat: "Content"),
                P("icon",     "Icon",     PropType.Icon,              cat: "Content"),
                P("disabled", "Disabled", PropType.Bool,   false,     cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDropdownItem", Category = "Dropdowns", DisplayName = "Dropdown Item",
            DefaultWidth = 160, DefaultHeight = 32,
            Props =
            [
                P("label",    "Label",    PropType.String, "Item",  cat: "Content"),
                P("icon",     "Icon",     PropType.Icon,            cat: "Content"),
                P("disabled", "Disabled", PropType.Bool,   false,   cat: "Behavior"),
            ]
        };
    }

    // ── DATA DISPLAY ──────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> DataDisplay()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmDivider", Category = "Data Display", DisplayName = "Divider",
            DefaultWidth = 200, DefaultHeight = 12,
            Props = []
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmText", Category = "Data Display", DisplayName = "Text",
            DefaultWidth = 120, DefaultHeight = 24,
            Props =
            [
                P("text",  "Text",  PropType.String, "Text", cat: "Content"),
                P("align", "Align", PropType.Enum,   "left", cat: "Appearance",
                    opts: ["left","center","right"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCard", Category = "Data Display", DisplayName = "Card",
            DefaultWidth = 280, DefaultHeight = 180,
            Props =
            [
                P("title",               "Title",                PropType.String, "Card Title", cat: "Content"),
                P("showHeader",          "Show Header",          PropType.Bool,   true,         cat: "Appearance"),
                P("showFooter",          "Show Footer",          PropType.Bool,   false,        cat: "Appearance"),
                P("variant",             "Variant",              PropType.Enum,   "default",    cat: "Appearance",
                    opts: ["default","elevated","outlined"]),
                P("headerIcon",          "Header Icon",          PropType.Icon,                 cat: "Content"),
                P("showPrimaryAction",   "Show Primary Action",  PropType.Bool,   true,         cat: "Appearance"),
                P("showSecondaryAction", "Show Secondary Action",PropType.Bool,   true,         cat: "Appearance"),
                P("primaryActionLabel",  "Primary Action Label", PropType.String, "Save",       cat: "Content"),
                P("secondaryActionLabel","Secondary Action Label",PropType.String,"Cancel",     cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmStatCard", Category = "Data Display", DisplayName = "Stat Card",
            DefaultWidth = 160, DefaultHeight = 100,
            Props =
            [
                P("title",         "Title",           PropType.String, "Total Revenue", cat: "Content"),
                P("value",         "Value",           PropType.String, "12 450",        cat: "Content"),
                P("unit",          "Unit",            PropType.String, "Kč",            cat: "Content"),
                P("trend",         "Trend",           PropType.Enum,   "up",            cat: "Appearance",
                    opts: ["up","down","neutral"]),
                P("subValue",      "Sub Value",       PropType.String, "",              cat: "Content"),
                P("subValueColor", "Sub Value Color", PropType.String, "",              cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmBadge", Category = "Data Display", DisplayName = "Badge",
            DefaultWidth = 60, DefaultHeight = 22, SizePresets = BadgeSizes,
            Props =
            [
                P("label",   "Label",   PropType.String, "Badge",   cat: "Content"),
                P("variant", "Variant", PropType.Enum,   "default", cat: "Appearance",
                    opts: ["default","primary","success","danger","warning","info"]),
                P("size",    "Size",    PropType.Enum,   "md",      cat: "Appearance",
                    opts: ["sm","md","lg"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmChip", Category = "Data Display", DisplayName = "Chip",
            DefaultWidth = 80, DefaultHeight = 24,
            Props =
            [
                P("label",     "Label",     PropType.String, "Chip",    cat: "Content"),
                P("removable", "Removable", PropType.Bool,   true,      cat: "Behavior"),
                P("variant",   "Variant",   PropType.Enum,   "default", cat: "Appearance",
                    opts: ["default","primary","success","danger","warning"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmChipGroup", Category = "Data Display", DisplayName = "Chip Group",
            DefaultWidth = 240, DefaultHeight = 32,
            Props = [P("chips", "Chips", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFilterChip", Category = "Data Display", DisplayName = "Filter Chip",
            DefaultWidth = 120, DefaultHeight = 28,
            Props =
            [
                P("label",     "Label",     PropType.String, "Filter", cat: "Content"),
                P("active",    "Active",    PropType.Bool,   true,     cat: "State"),
                P("removable", "Removable", PropType.Bool,   true,     cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmAccordion", Category = "Data Display", DisplayName = "Accordion",
            DefaultWidth = 280, DefaultHeight = 120,
            Props =
            [
                P("items",    "Items",          PropType.StringList, cat: "Content"),
                P("multiple", "Allow Multiple", PropType.Bool, false, cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmAccordionItem", Category = "Data Display", DisplayName = "Accordion Item",
            DefaultWidth = 280, DefaultHeight = 44,
            Props =
            [
                P("title",    "Title",    PropType.String, "Section", cat: "Content"),
                P("expanded", "Expanded", PropType.Bool,   false,     cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmEmptyState", Category = "Data Display", DisplayName = "Empty State",
            DefaultWidth = 280, DefaultHeight = 160,
            Props =
            [
                P("title",       "Title",        PropType.String, "No data",                        cat: "Content"),
                P("description", "Description",  PropType.String, "There is nothing to display here.", cat: "Content"),
                P("actionLabel", "Action Label", PropType.String, "Add item",                       cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmChangeDiff", Category = "Data Display", DisplayName = "Change Diff",
            DefaultWidth = 400, DefaultHeight = 120,
            Props =
            [
                P("oldValue", "Old Value", PropType.String, "Old value", cat: "Content"),
                P("newValue", "New Value", PropType.String, "New value", cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmKanbanBoard", Category = "Data Display", DisplayName = "Kanban Board",
            DefaultWidth = 480, DefaultHeight = 320,
            Props =
            [
                P("columns",      "Columns",       PropType.StringList, cat: "Content"),
                P("showAddCard",  "Show Add Card", PropType.Bool, true, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmMultiViewList", Category = "Data Display", DisplayName = "Multi View List",
            DefaultWidth = 360, DefaultHeight = 280,
            Props =
            [
                P("title",      "Title",       PropType.String, "Items", cat: "Content"),
                P("showSearch", "Show Search", PropType.Bool,   true,    cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmQRCode", Category = "Data Display", DisplayName = "QR Code",
            DefaultWidth = 120, DefaultHeight = 120,
            Props =
            [
                P("value", "Value", PropType.String, "https://example.com", cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmBarcode", Category = "Data Display", DisplayName = "Barcode",
            DefaultWidth = 200, DefaultHeight = 80,
            Props =
            [
                P("value",  "Value",  PropType.String, "1234567890", cat: "Content"),
                P("format", "Format", PropType.Enum,   "CODE128",    cat: "Appearance",
                    opts: ["CODE128","EAN13","QR"]),
            ]
        };
    }

    // ── DATA TABLE ────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> DataTable()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmDataTable", Category = "Data Table", DisplayName = "Data Table",
            DefaultWidth = 600, DefaultHeight = 320,
            Props =
            [
                P("title",           "Title",             PropType.String,     "",    cat: "Content"),
                P("columns",         "Columns",           PropType.StringList, cat: "Content"),
                P("rows",            "Rows",              PropType.Int,        5,     cat: "Appearance"),
                P("showSearch",      "Show Search",       PropType.Bool,       true,  cat: "Appearance"),
                P("showPagination",  "Show Pagination",   PropType.Bool,       true,  cat: "Appearance"),
                P("showBulkActions", "Show Bulk Actions", PropType.Bool,       false, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmPagination", Category = "Data Table", DisplayName = "Pagination",
            DefaultWidth = 240, DefaultHeight = 36,
            Props =
            [
                P("totalPages",  "Total Pages",  PropType.Int, 5, cat: "Content"),
                P("currentPage", "Current Page", PropType.Int, 1, cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmBulkActionBar", Category = "Data Table", DisplayName = "Bulk Action Bar",
            DefaultWidth = 400, DefaultHeight = 44,
            Props = [P("selectedCount", "Selected Count", PropType.Int, 3, cat: "State")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmColumnFilter", Category = "Data Table", DisplayName = "Column Filter",
            DefaultWidth = 180, DefaultHeight = 120,
            Props =
            [
                P("columnName", "Column Name", PropType.String, "Name", cat: "Content"),
                P("filterType", "Filter Type", PropType.Enum,   "text", cat: "Appearance",
                    opts: ["text","select","date","number"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmColumnPicker", Category = "Data Table", DisplayName = "Column Picker",
            DefaultWidth = 160, DefaultHeight = 160,
            Props = [P("columns", "Columns", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmViewManager", Category = "Data Table", DisplayName = "View Manager",
            DefaultWidth = 200, DefaultHeight = 40,
            Props =
            [
                P("viewName",       "View Name",        PropType.String, "Default view", cat: "Content"),
                P("showSaveButton", "Show Save Button", PropType.Bool,   true,           cat: "Appearance"),
            ]
        };
    }

    // ── FEEDBACK ──────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Feedback()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmAlert", Category = "Feedback", DisplayName = "Alert",
            DefaultWidth = 400, DefaultHeight = 56,
            Props =
            [
                P("message",       "Message",        PropType.String, "This is an alert message.", cat: "Content"),
                P("title",         "Title",          PropType.String, "",                          cat: "Content"),
                P("variant",       "Variant",        PropType.Enum,   "info",                      cat: "Appearance",
                    opts: ["info","success","warning","danger"]),
                P("visualVariant", "Visual Variant", PropType.Enum,   "soft",                      cat: "Appearance",
                    opts: ["soft","filled","outlined"]),
                P("icon",          "Icon",           PropType.Icon,                                cat: "Content"),
                P("dismissible",   "Dismissible",    PropType.Bool,   true,                        cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFocusScope", Category = "Feedback", DisplayName = "Focus Scope",
            IsContainer = true,
            DefaultWidth = 280, DefaultHeight = 120,
            Props = [P("ariaLabel", "Label", PropType.String, "Focus scope", cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSheetHandle", Category = "Feedback", DisplayName = "Sheet Handle",
            DefaultWidth = 120, DefaultHeight = 24
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmModal", Category = "Feedback", DisplayName = "Modal",
            DefaultWidth = 480, DefaultHeight = 360,
            Props =
            [
                P("title",      "Title",       PropType.String, "Modal Title", cat: "Content"),
                P("showFooter", "Show Footer", PropType.Bool,   true,          cat: "Appearance"),
                P("size",       "Size",        PropType.Enum,   "medium",      cat: "Appearance",
                    opts: ["small","medium","large","xLarge","fullscreen"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDialog", Category = "Feedback", DisplayName = "Dialog",
            DefaultWidth = 320, DefaultHeight = 160,
            Props =
            [
                P("title",   "Title",   PropType.String, "Confirm action", cat: "Content"),
                P("message", "Message", PropType.String, "Are you sure?",  cat: "Content"),
                P("variant", "Variant", PropType.Enum,   "info",           cat: "Appearance",
                    opts: ["info","success","warning","error"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmTooltip", Category = "Feedback", DisplayName = "Tooltip",
            DefaultWidth = 120, DefaultHeight = 36,
            Props =
            [
                P("text",      "Text",      PropType.String, "Tooltip text", cat: "Content"),
                P("placement", "Placement", PropType.Enum,   "top",          cat: "Appearance",
                    opts: ["top","bottom","left","right"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmPopover", Category = "Feedback", DisplayName = "Popover",
            DefaultWidth = 200, DefaultHeight = 120,
            Props =
            [
                P("title",     "Title",     PropType.String, "Popover", cat: "Content"),
                P("placement", "Placement", PropType.Enum,   "bottom",  cat: "Appearance",
                    opts: ["top","bottom","left","right"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmProgressBar", Category = "Feedback", DisplayName = "Progress Bar",
            DefaultWidth = 240, DefaultHeight = 16,
            Props =
            [
                P("value",         "Value",         PropType.Double, 60.0,      cat: "State"),
                P("max",           "Max",           PropType.Double, 100.0,     cat: "State"),
                P("size",          "Size",          PropType.Enum,   "md",      cat: "Appearance",
                    opts: ["sm","md","lg"]),
                P("variant",       "Variant",       PropType.Enum,   "default", cat: "Appearance",
                    opts: ["default","success","warning","error","gradient"]),
                P("indeterminate", "Indeterminate", PropType.Bool,   false,     cat: "State"),
                P("striped",       "Striped",       PropType.Bool,   false,     cat: "Appearance"),
                P("showLabel",     "Show Label",    PropType.Bool,   false,     cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSpinner", Category = "Feedback", DisplayName = "Spinner",
            DefaultWidth = 32, DefaultHeight = 32, SizePresets = SpinnerSizes,
            Props = [P("size", "Size", PropType.Enum, "md", cat: "Appearance", opts: ["sm","md","lg"])]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSkeleton", Category = "Feedback", DisplayName = "Skeleton",
            DefaultWidth = 280, DefaultHeight = 80,
            Props =
            [
                P("lines",      "Lines",       PropType.Int,  3,     cat: "Appearance"),
                P("showAvatar", "Show Avatar", PropType.Bool, false, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmToastContainer", Category = "Feedback", DisplayName = "Toast Container",
            DefaultWidth = 320, DefaultHeight = 120,
            Props =
            [
                P("position",   "Position",   PropType.Enum, "topRight", cat: "Appearance",
                    opts: ["topRight","topLeft","bottomRight","bottomLeft"]),
                P("maxVisible", "Max Visible", PropType.Int, 3,          cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmAutoSaveIndicator", Category = "Feedback", DisplayName = "Auto Save Indicator",
            DefaultWidth = 100, DefaultHeight = 24,
            Props =
            [
                P("state", "State", PropType.Enum, "saved", cat: "State",
                    opts: ["saving","saved","error"]),
            ]
        };
    }

    // ── NOTIFICATIONS ─────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Notifications()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmNotificationBell", Category = "Notifications", DisplayName = "Notification Bell",
            DefaultWidth = 48, DefaultHeight = 48,
            Props =
            [
                P("unreadCount", "Unread Count", PropType.Int,  3,     cat: "State"),
                P("disabled",    "Disabled",     PropType.Bool, false, cat: "Behavior"),
            ]
        };
    }

    // ── NAVIGATION ────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Navigation()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmTabs", Category = "Navigation", DisplayName = "Tabs",
            DefaultWidth = 400, DefaultHeight = 40,
            Props =
            [
                P("tabs",      "Tabs",             PropType.StringList, cat: "Content"),
                P("activeTab", "Active Tab Index", PropType.Int,        0,      cat: "State"),
                P("variant",   "Variant",          PropType.Enum,       "line", cat: "Appearance",
                    opts: ["line","pill","enclosed"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmBreadcrumbs", Category = "Navigation", DisplayName = "Breadcrumbs",
            DefaultWidth = 300, DefaultHeight = 24,
            Props = [P("items", "Items", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmTabPanel", Category = "Navigation", DisplayName = "Tab Panel",
            DefaultWidth = 400, DefaultHeight = 160,
            Props = [P("label", "Label", PropType.String, "Tab content", cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmContextMenu", Category = "Navigation", DisplayName = "Context Menu",
            DefaultWidth = 160, DefaultHeight = 120,
            Props = [P("items", "Items", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmContextMenuItem", Category = "Navigation", DisplayName = "Context Menu Item",
            DefaultWidth = 160, DefaultHeight = 32,
            Props =
            [
                P("text",     "Text",     PropType.String, "Item",  cat: "Content"),
                P("disabled", "Disabled", PropType.Bool,   false,   cat: "Behavior"),
                P("danger",   "Danger",   PropType.Bool,   false,   cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmBottomNavigation", Category = "Navigation", DisplayName = "Bottom Navigation",
            DefaultWidth = 360, DefaultHeight = 56,
            Props =
            [
                P("items",      "Items",        PropType.StringList, cat: "Content"),
                P("activeIndex","Active Index", PropType.Int, 0,     cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmMobileActionBar", Category = "Navigation", DisplayName = "Mobile Action Bar",
            DefaultWidth = 360, DefaultHeight = 60,
            Props =
            [
                P("items",      "Items",      PropType.StringList, cat: "Content"),
                P("maxVisible", "Max Visible", PropType.Int, 3,     cat: "Behavior"),
                P("placement",  "Placement",  PropType.Enum, "stickyContainer", cat: "Appearance",
                    opts: ["stickyContainer", "fixedViewport", "inline"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmMenu", Category = "Navigation", DisplayName = "Menu",
            DefaultWidth = 200, DefaultHeight = 180,
            Props =
            [
                P("items",     "Items",     PropType.StringList, cat: "Content"),
                P("showIcons", "Show Icons",PropType.Bool, true, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmNavigationGuard", Category = "Navigation", DisplayName = "Navigation Guard",
            DefaultWidth = 160, DefaultHeight = 32,
            Props =
            [
                P("isDirty", "Is Dirty", PropType.Bool, false, cat: "State"),
                P("enabled", "Enabled", PropType.Bool, true, cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmScrollSpyNav", Category = "Navigation", DisplayName = "Scroll Spy Nav",
            DefaultWidth = 200, DefaultHeight = 180,
            Props =
            [
                P("title",           "Title",             PropType.String,     "",       cat: "Content"),
                P("items",           "Items",             PropType.StringList, cat: "Content"),
                P("variant",         "Variant",           PropType.Enum, "sideRail", cat: "Appearance",
                    opts: ["sideRail","breadcrumb"]),
                P("enableScrollSpy", "Enable Scroll Spy", PropType.Bool,  false, cat: "Behavior"),
            ]
        };
    }

    // ── LAYOUT ────────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Layout()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmTopBar", Category = "Layout", DisplayName = "Top Bar",
            DefaultWidth = 800, DefaultHeight = 56,
            Props =
            [
                P("title",             "Title",              PropType.String, "App Name", cat: "Content"),
                P("showSearch",        "Show Search",        PropType.Bool,   true,       cat: "Appearance"),
                P("showNotifications", "Show Notifications", PropType.Bool,   true,       cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSidebar", Category = "Layout", DisplayName = "Sidebar",
            DefaultWidth = 220, DefaultHeight = 400,
            Props =
            [
                P("items",     "Items",     PropType.StringList, cat: "Content"),
                P("collapsed", "Collapsed", PropType.Bool,       false, cat: "State"),
                P("width",     "Width",     PropType.Int,        220,   cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDrawer", Category = "Layout", DisplayName = "Drawer",
            DefaultWidth = 360, DefaultHeight = 400,
            Props =
            [
                P("title",     "Title",     PropType.String, "Drawer", cat: "Content"),
                P("placement", "Placement", PropType.Enum,   "right",  cat: "Appearance",
                    opts: ["left","right"]),
                P("width",     "Width",     PropType.Int,    400,      cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSection", Category = "Layout", DisplayName = "Section",
            DefaultWidth = 400, DefaultHeight = 160,
            Props = [P("title", "Title", PropType.String, "Section Title", cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCommandPalette", Category = "Layout", DisplayName = "Command Palette",
            DefaultWidth = 480, DefaultHeight = 320,
            Props = [P("placeholder", "Placeholder", PropType.String, "Type a command...", cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmKeyboardShortcutsHelp", Category = "Layout", DisplayName = "Keyboard Shortcuts",
            DefaultWidth = 360, DefaultHeight = 280,
            Props = [P("shortcuts", "Shortcuts", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmLayoutObserver", Category = "Layout", DisplayName = "Layout Observer",
            IsContainer = true,
            DefaultWidth = 320, DefaultHeight = 180,
            Props =
            [
                P("layoutMode", "Layout Mode", PropType.Enum, "auto", cat: "Behavior",
                    opts: ["auto", "desktop", "tablet", "mobile"]),
                P("isContainer", "Is Container", PropType.Bool, true, cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmStackLayout", Category = "Layout", DisplayName = "Stack Layout",
            DefaultWidth = 240, DefaultHeight = 160,
            Props =
            [
                P("direction", "Direction", PropType.Enum, "vertical", cat: "Appearance",
                    opts: ["vertical","horizontal"]),
                P("gap",      "Gap",       PropType.Int,  8,           cat: "Appearance"),
                P("items",    "Items",     PropType.Int,  3,           cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSplitter", Category = "Layout", DisplayName = "Splitter",
            DefaultWidth = 320, DefaultHeight = 180,
            Props =
            [
                P("orientation", "Orientation", PropType.Enum, "horizontal", cat: "Appearance",
                    opts: ["horizontal","vertical"]),
                P("pane1Label",  "Pane 1 Label",PropType.String,"Pane 1",    cat: "Content"),
                P("pane2Label",  "Pane 2 Label",PropType.String,"Pane 2",    cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDockManager", Category = "Layout", DisplayName = "Dock Manager",
            DefaultWidth = 360, DefaultHeight = 220,
            Props =
            [
                P("showLeft",   "Show Left Panel",   PropType.Bool, true,  cat: "Appearance"),
                P("showBottom", "Show Bottom Panel", PropType.Bool, true,  cat: "Appearance"),
            ]
        };
    }

    // ── TOOLBAR ───────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Toolbar()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmToolbar", Category = "Toolbar", DisplayName = "Toolbar",
            DefaultWidth = 600, DefaultHeight = 48,
            Props =
            [
                P("title",  "Title",  PropType.String, "",    cat: "Content"),
                P("sticky", "Sticky", PropType.Bool,   false, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmToolbarButton", Category = "Toolbar", DisplayName = "Toolbar Button",
            DefaultWidth = 80, DefaultHeight = 32,
            Props =
            [
                P("label",    "Label",    PropType.String, "Action", cat: "Content"),
                P("icon",     "Icon",     PropType.Icon,             cat: "Content"),
                P("disabled", "Disabled", PropType.Bool,   false,    cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmToolbarDivider", Category = "Toolbar", DisplayName = "Toolbar Divider",
            DefaultWidth = 1, DefaultHeight = 32,
            Props = []
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFormActionBar", Category = "Toolbar", DisplayName = "Form Action Bar",
            DefaultWidth = 600, DefaultHeight = 56,
            Props =
            [
                P("position",     "Position",       PropType.Enum, "static", cat: "Appearance",
                    opts: ["static","stickyTop","floatingBottom"]),
                P("showOnScroll", "Show On Scroll", PropType.Bool,  false,   cat: "Behavior"),
                P("statusText",   "Status Text",    PropType.String, "",     cat: "Content"),
                P("primaryLabel", "Primary Label",  PropType.String, "Save", cat: "Content"),
            ]
        };
    }

    // ── FORMS ─────────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Forms()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmFormSection", Category = "Forms", DisplayName = "Form Section",
            DefaultWidth = 500, DefaultHeight = 140,
            Props =
            [
                P("title",       "Title",       PropType.String, "Section",             cat: "Content"),
                P("description", "Description", PropType.String, "Section description.", cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFormRow", Category = "Forms", DisplayName = "Form Row",
            DefaultWidth = 500, DefaultHeight = 56,
            Props =
            [
                P("label",    "Label",    PropType.String, "Field Label", cat: "Content"),
                P("required", "Required", PropType.Bool,   false,         cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFormField", Category = "Forms", DisplayName = "Form Field",
            DefaultWidth = 280, DefaultHeight = 64,
            Props =
            [
                P("label",        "Label",        PropType.String, "Label", cat: "Content"),
                P("required",     "Required",     PropType.Bool,   false,   cat: "Behavior"),
                P("disabled",     "Disabled",     PropType.Bool,   false,   cat: "Behavior"),
                P("helpText",     "Help Text",    PropType.String, "",      cat: "Content"),
                P("errorMessage", "Error Message",PropType.String, "",      cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmInlineEdit", Category = "Forms", DisplayName = "Inline Edit",
            DefaultWidth = 200, DefaultHeight = 28,
            Props =
            [
                P("value",       "Value",        PropType.String, "Click to edit", cat: "Content"),
                P("editOnClick", "Edit on Click", PropType.Bool,  true,            cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmValidationSummary", Category = "Forms", DisplayName = "Validation Summary",
            DefaultWidth = 320, DefaultHeight = 100,
            Props = [P("errors", "Errors", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDynamicFormRenderer", Category = "Forms", DisplayName = "Dynamic Form",
            DefaultWidth = 400, DefaultHeight = 240,
            Props = [P("fieldCount", "Field Count", PropType.Int, 4, cat: "Appearance")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmValidatedField", Category = "Forms", DisplayName = "Validated Field",
            DefaultWidth = 280, DefaultHeight = 64,
            Props =
            [
                P("label",             "Label",              PropType.String, "Label", cat: "Content"),
                P("required",          "Required",           PropType.Bool,   false,   cat: "Behavior"),
                P("valid",             "Valid",              PropType.Bool,   true,    cat: "State"),
                P("validationMessage", "Validation Message", PropType.String, "",      cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFormValidationMessage", Category = "Forms", DisplayName = "Validation Message",
            DefaultWidth = 280, DefaultHeight = 24,
            Props =
            [
                P("message",  "Message",  PropType.String, "Validation message", cat: "Content"),
                P("severity", "Severity", PropType.Enum,   "error",              cat: "Appearance",
                    opts: ["error","warning","info"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFormulaBuilder", Category = "Forms", DisplayName = "Formula Builder",
            DefaultWidth = 300, DefaultHeight = 160,
            Props =
            [
                P("formula",    "Formula",    PropType.String, "SUM(A1:A5)", cat: "Content"),
                P("showResult", "Show Result",PropType.Bool,   true,         cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmConditionBuilder", Category = "Forms", DisplayName = "Condition Builder",
            DefaultWidth = 320, DefaultHeight = 180,
            Props =
            [
                P("conditions",    "Conditions",     PropType.Int,  2,     cat: "Appearance"),
                P("groupOperator", "Group Operator", PropType.Enum, "AND", cat: "Appearance",
                    opts: ["AND","OR"]),
            ]
        };
    }

    // ── FILES ─────────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Files()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmFileDropZone", Category = "Files", DisplayName = "File Drop Zone",
            DefaultWidth = 300, DefaultHeight = 160,
            Props =
            [
                P("label",    "Label",    PropType.String, "Drop files here or click to upload", cat: "Content"),
                P("accept",   "Accept",   PropType.String, "*/*",  cat: "Behavior"),
                P("multiple", "Multiple", PropType.Bool,   true,   cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmAttachmentManager", Category = "Files", DisplayName = "Attachment Manager",
            DefaultWidth = 360, DefaultHeight = 200,
            Props = [P("maxFiles", "Max Files", PropType.Int, 5, cat: "Behavior")]
        };
    }

    // ── CHARTS ────────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Charts()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmChart", Category = "Charts", DisplayName = "Chart",
            DefaultWidth = 400, DefaultHeight = 240,
            Props =
            [
                P("title",      "Title",       PropType.String, "Chart Title", cat: "Content"),
                P("type",       "Type",        PropType.Enum,   "bar",         cat: "Appearance",
                    opts: ["bar","horizontalBar","line","area","pie","donut","funnel","heatmap","treemap","stackedBar","stackedHorizontalBar","stackedArea"]),
                P("dataPoints", "Data Points", PropType.Int,    6,             cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSankeyChart", Category = "Charts", DisplayName = "Sankey Chart",
            DefaultWidth = 400, DefaultHeight = 240,
            Props =
            [
                P("showLabels", "Show Labels", PropType.Bool, true, cat: "Appearance"),
                P("showValues", "Show Values", PropType.Bool, true, cat: "Appearance"),
                P("nodeWidth", "Node Width", PropType.Double, 16.0, cat: "Appearance"),
                P("nodePadding", "Node Padding", PropType.Double, 10.0, cat: "Appearance"),
                P("minLinkWidth", "Minimum Link Width", PropType.Double, 1.0, cat: "Appearance"),
                P("linkOpacity", "Link Opacity", PropType.Double, 0.4, cat: "Appearance"),
                P("highlightOnHover", "Highlight on Hover", PropType.Bool, true, cat: "Behavior"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCalendarHeatmap", Category = "Charts", DisplayName = "Calendar Heatmap",
            DefaultWidth = 420, DefaultHeight = 140,
            Props =
            [
                P("year", "Year", PropType.Int, 2025, cat: "Content"),
                P("maxValue", "Maximum Value", PropType.Double, cat: "Behavior"),
                P("levels", "Levels", PropType.Int, 5, cat: "Appearance"),
                P("colorScheme", "Color Scheme", PropType.Enum, "Primary", cat: "Appearance",
                    opts: ["Primary","Success","Danger","Neutral"]),
                P("cellSize", "Cell Size", PropType.Int, 12, cat: "Appearance"),
                P("showMonthLabels", "Show Month Labels", PropType.Bool, true, cat: "Appearance"),
                P("showDayLabels", "Show Day Labels", PropType.Bool, true, cat: "Appearance"),
                P("showLegend", "Show Legend", PropType.Bool, true, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSparkline", Category = "Charts", DisplayName = "Sparkline",
            DefaultWidth = 120, DefaultHeight = 32,
            Props =
            [
                P("type",  "Type",  PropType.Enum, "line", cat: "Appearance",
                    opts: ["line","area","bar"]),
                P("color", "Color", PropType.Color, "#3b82f6", cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmGauge", Category = "Charts", DisplayName = "Gauge",
            DefaultWidth = 140, DefaultHeight = 100,
            Props =
            [
                P("value",  "Value",  PropType.Double, 65.0,  cat: "State"),
                P("min",    "Min",    PropType.Double, 0.0,   cat: "Behavior"),
                P("max",    "Max",    PropType.Double, 100.0, cat: "Behavior"),
                P("label",  "Label",  PropType.String, "",    cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmStockChart", Category = "Charts", DisplayName = "Stock Chart",
            DefaultWidth = 320, DefaultHeight = 180,
            Props =
            [
                P("title",   "Title",  PropType.String, "ACME",  cat: "Content"),
                P("type",    "Type",   PropType.Enum,   "candle",cat: "Appearance",
                    opts: ["candle","area","line"]),
                P("period",  "Period", PropType.Enum,   "1M",    cat: "Appearance",
                    opts: ["1D","1W","1M","3M","1Y"]),
            ]
        };
    }

    // ── WORKFLOW ─────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Workflow()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmWorkflowToolbox", Category = "Workflow", DisplayName = "Workflow Toolbox",
            DefaultWidth = 160, DefaultHeight = 280,
            Props = [P("nodes", "Nodes", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmWorkflowPropertiesPanel", Category = "Workflow", DisplayName = "Properties Panel",
            DefaultWidth = 220, DefaultHeight = 300,
            Props =
            [
                P("title",    "Title",     PropType.String, "Properties", cat: "Content"),
                P("nodeType", "Node Type", PropType.String, "Task",       cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmWorkflowMinimap", Category = "Workflow", DisplayName = "Workflow Minimap",
            DefaultWidth = 160, DefaultHeight = 120,
            Props = [P("scale", "Scale", PropType.Double, 0.2, cat: "Appearance")]
        };
    }

    // ── COMPLEX ───────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Complex()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmTimeline", Category = "Complex", DisplayName = "Timeline",
            DefaultWidth = 300, DefaultHeight = 240,
            Props =
            [
                P("items",       "Items",       PropType.StringList, cat: "Content"),
                P("orientation", "Orientation", PropType.Enum, "vertical", cat: "Appearance",
                    opts: ["vertical","horizontal"]),
                P("alternate",   "Alternate",   PropType.Bool, false, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmStepper", Category = "Complex", DisplayName = "Stepper",
            DefaultWidth = 500, DefaultHeight = 56,
            Props =
            [
                P("steps",       "Steps",            PropType.StringList, cat: "Content"),
                P("activeStep",  "Active Step Index",PropType.Int, 1,    cat: "State"),
                P("orientation", "Orientation",      PropType.Enum, "horizontal", cat: "Appearance",
                    opts: ["horizontal","vertical"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmScheduler", Category = "Complex", DisplayName = "Scheduler",
            DefaultWidth = 700, DefaultHeight = 400,
            Props =
            [
                P("title", "Title", PropType.String, "Schedule", cat: "Content"),
                P("view",  "View",  PropType.Enum,   "week",     cat: "Appearance",
                    opts: ["day","week","month","agenda","timeline"]),
                P("showPrint", "Show Print", PropType.Bool, true, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDashboard", Category = "Complex", DisplayName = "Dashboard",
            DefaultWidth = 800, DefaultHeight = 500,
            Props =
            [
                P("layoutMode",    "Layout Mode",    PropType.Enum, "auto", cat: "Behavior",
                    opts: ["auto", "desktop", "tablet", "mobile"]),
                P("columns",       "Columns",        PropType.Int,  3,     cat: "Appearance"),
                P("rows",          "Rows",           PropType.Int,  2,     cat: "Appearance"),
                P("editable",      "Editable",       PropType.Bool, false, cat: "Behavior"),
                P("showAddWidget", "Show Add Widget",PropType.Bool, false, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmMarkdownEditor", Category = "Complex", DisplayName = "Markdown Editor",
            DefaultWidth = 500, DefaultHeight = 300,
            Props =
            [
                P("placeholder",  "Placeholder",  PropType.String, "Write markdown...", cat: "Content"),
                P("showToolbar",  "Show Toolbar", PropType.Bool,   true,                cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCodeEditor", Category = "Complex", DisplayName = "Code Editor",
            DefaultWidth = 500, DefaultHeight = 260,
            Props =
            [
                P("language",    "Language",    PropType.String, "json", cat: "Content"),
                P("label",       "Label",       PropType.String,         cat: "Content"),
                P("placeholder", "Placeholder", PropType.String,         cat: "Content"),
                // Wrapping is a visible layout decision (prose wraps, source code scrolls sideways),
                // so a wireframe has to be able to state it — mirrors TmCodeEditor.Wrap.
                P("wrap",        "Wrap lines",  PropType.Bool,   false,   cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmRichEditorFull", Category = "Complex", DisplayName = "Rich Text Editor (Full)",
            DefaultWidth = 600, DefaultHeight = 320,
            Props = [P("placeholder", "Placeholder", PropType.String, "Start writing...", cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmRichEditorSimple", Category = "Complex", DisplayName = "Rich Text Editor (Simple)",
            DefaultWidth = 400, DefaultHeight = 200,
            Props = [P("placeholder", "Placeholder", PropType.String, "Type here...", cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmImageGallery", Category = "Complex", DisplayName = "Image Gallery",
            DefaultWidth = 400, DefaultHeight = 280,
            Props =
            [
                P("columns",   "Columns",    PropType.Int,  3,      cat: "Appearance"),
                P("itemCount", "Item Count", PropType.Int,  6,      cat: "Appearance"),
                P("layout",    "Layout",     PropType.Enum, "grid", cat: "Appearance",
                    opts: ["grid","masonry","list"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmImportWizard", Category = "Complex", DisplayName = "Import Wizard",
            DefaultWidth = 560, DefaultHeight = 360,
            Props = [P("steps", "Steps", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFilterBuilder", Category = "Complex", DisplayName = "Filter Builder",
            DefaultWidth = 520, DefaultHeight = 192,
            Props =
            [
                P("conditions",    "Conditions",     PropType.Int,  3,     cat: "Appearance"),
                P("groupOperator", "Group Operator", PropType.Enum, "AND", cat: "Appearance",
                    opts: ["AND","OR"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmActivityLog", Category = "Complex", DisplayName = "Activity Log",
            DefaultWidth = 400, DefaultHeight = 280,
            Props = [P("itemCount", "Item Count", PropType.Int, 5, cat: "Appearance")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmActivityComments", Category = "Complex", DisplayName = "Activity Comments",
            DefaultWidth = 400, DefaultHeight = 320,
            Props = [P("commentCount", "Comment Count", PropType.Int, 3, cat: "Appearance")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmActivityAttachments", Category = "Complex", DisplayName = "Activity Attachments",
            DefaultWidth = 320, DefaultHeight = 120,
            Props = [P("files", "Files", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmActivityTimeline", Category = "Complex", DisplayName = "Activity Timeline",
            DefaultWidth = 320, DefaultHeight = 160,
            Props = [P("events", "Events", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmLightbox", Category = "Complex", DisplayName = "Lightbox",
            DefaultWidth = 600, DefaultHeight = 400,
            Props =
            [
                P("imageCount",   "Image Count",   PropType.Int, 8, cat: "Content"),
                P("currentIndex", "Current Index", PropType.Int, 1, cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmExportOptions", Category = "Complex", DisplayName = "Export Options",
            DefaultWidth = 360, DefaultHeight = 240,
            Props = [P("formats", "Formats", PropType.StringList, cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmImportPreview", Category = "Complex", DisplayName = "Import Preview",
            DefaultWidth = 480, DefaultHeight = 260,
            Props =
            [
                P("rows", "Rows",    PropType.Int, 4, cat: "Appearance"),
                P("cols", "Columns", PropType.Int, 4, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmTreeView", Category = "Complex", DisplayName = "Tree View",
            DefaultWidth = 240, DefaultHeight = 200,
            Props =
            [
                P("depth",           "Depth",           PropType.Int,  3,     cat: "Appearance"),
                P("showCheckboxes",  "Show Checkboxes", PropType.Bool, false, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmWorkflowDesignerCanvas", Category = "Complex", DisplayName = "Workflow Designer",
            DefaultWidth = 500, DefaultHeight = 300,
            Props = [P("title", "Title", PropType.String, "Workflow", cat: "Content")]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmPdfViewer", Category = "Complex", DisplayName = "PDF Viewer",
            DefaultWidth = 360, DefaultHeight = 460,
            Props =
            [
                P("fileName",          "File Name",          PropType.String, "document.pdf", cat: "Content"),
                P("pageCount",         "Page Count",         PropType.Int,    12,             cat: "Content"),
                P("currentPage",       "Current Page",       PropType.Int,    1,              cat: "State"),
                P("showToolbar",       "Show Toolbar",       PropType.Bool,   true,           cat: "Appearance"),
                P("showSearch",        "Show Search",        PropType.Bool,   false,          cat: "Appearance"),
                P("enableAnnotations", "Enable Annotations", PropType.Bool,   false,          cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmPdfAnnotator", Category = "Complex", DisplayName = "PDF Annotator",
            DefaultWidth = 520, DefaultHeight = 460,
            Props =
            [
                P("fileName",        "File Name",        PropType.String, "document.pdf", cat: "Content"),
                P("annotationCount", "Annotation Count", PropType.Int,    3,              cat: "Content"),
                P("mode",            "Mode",             PropType.Enum,   "browse",
                    opts: ["browse", "highlight", "comment", "stamp", "draw"], cat: "State"),
                P("showPanel",       "Show Panel",       PropType.Bool,   true,           cat: "Appearance"),
                P("allowExport",     "Allow Export",     PropType.Bool,   true,           cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmMap", Category = "Complex", DisplayName = "Map",
            DefaultWidth = 420, DefaultHeight = 280,
            Props =
            [
                P("markerCount",     "Marker Count",      PropType.Int,  5,     cat: "Content"),
                P("showClusters",    "Show Clusters",     PropType.Bool, false, cat: "Appearance"),
                P("showZoomControl", "Show Zoom Control", PropType.Bool, true,  cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCommentComposer", Category = "Complex", DisplayName = "Comment Composer",
            DefaultWidth = 300, DefaultHeight = 88,
            Props =
            [
                P("placeholder", "Placeholder", PropType.String, "Write a comment…", cat: "Content"),
                P("showAvatar",  "Show Avatar",  PropType.Bool,   true,              cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmCommentReactions", Category = "Complex", DisplayName = "Comment Reactions",
            DefaultWidth = 160, DefaultHeight = 28,
            Props =
            [
                P("reactions", "Reactions", PropType.StringList, cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmReactionPicker", Category = "Complex", DisplayName = "Reaction Picker",
            DefaultWidth = 200, DefaultHeight = 80,
            Props =
            [
                P("columns", "Columns", PropType.Int, 8, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmShareLinkPanel", Category = "Complex", DisplayName = "Share Link Panel",
            DefaultWidth = 320, DefaultHeight = 120,
            Props =
            [
                P("link",       "Link",         PropType.String, "https://app.example.com/share/abc123", cat: "Content"),
                P("showRole",   "Show Role",    PropType.Bool,   true, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSubmissionStatusTimeline", Category = "Complex", DisplayName = "Submission Timeline",
            DefaultWidth = 260, DefaultHeight = 200,
            Props =
            [
                P("statusCount", "Status Count", PropType.Int, 4, cat: "Appearance"),
                P("activeIndex", "Active Index", PropType.Int, 2, cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmAuditTrailViewer", Category = "Complex", DisplayName = "Audit Trail",
            DefaultWidth = 320, DefaultHeight = 200,
            Props =
            [
                P("rowCount", "Row Count", PropType.Int, 5, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmLedgerGrid", Category = "Complex", DisplayName = "Ledger Grid",
            DefaultWidth = 680, DefaultHeight = 380,
            Props =
            [
                P("rowCount",   "Row Count",   PropType.Int,    6,     cat: "Content"),
                P("currency",   "Currency",    PropType.String, "CZK", cat: "Content"),
                P("showFooter", "Show Footer", PropType.Bool,   true,  cat: "Appearance"),
                P("matching",   "Matching",    PropType.Bool,   true,  cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmMoneyDisplay", Category = "Basic", DisplayName = "Money Display",
            DefaultWidth = 140, DefaultHeight = 28,
            Props =
            [
                P("amount",   "Amount",   PropType.String, "1 234,56", cat: "Content"),
                P("currency", "Currency", PropType.String, "CZK",      cat: "Content"),
                P("negative", "Negative", PropType.Bool,   false,      cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmKycWizard", Category = "Complex", DisplayName = "KYC Wizard",
            DefaultWidth = 560, DefaultHeight = 420,
            Props =
            [
                P("stepCount",   "Step Count",   PropType.Int,  5,    cat: "Content"),
                P("activeStep",  "Active Step",  PropType.Int,  1,    cat: "State"),
                P("subjectKind", "Subject Kind", PropType.Enum, "person",
                    opts: ["person", "company"], cat: "Content"),
                P("showErrors",  "Show Errors",  PropType.Bool, false, cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDataImport", Category = "Complex", DisplayName = "Data Import",
            DefaultWidth = 620, DefaultHeight = 400,
            Props =
            [
                P("activeStep", "Active Step", PropType.Int, 0, cat: "State"),
                P("rowCount",   "Row Count",   PropType.Int, 6, cat: "Content"),
                P("errorCount", "Error Count", PropType.Int, 0, cat: "State"),
                P("progress",   "Progress",    PropType.Int, 0, cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmRedactionLayer", Category = "Complex", DisplayName = "Redaction Layer",
            DefaultWidth = 520, DefaultHeight = 380,
            Props =
            [
                P("areaCount", "Area Count", PropType.Int,  2,     cat: "Content"),
                P("applied",   "Applied",    PropType.Bool, false, cat: "State"),
                P("showPanel", "Show Panel", PropType.Bool, true,  cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmScreeningResultPanel", Category = "Complex", DisplayName = "Screening Results",
            DefaultWidth = 420, DefaultHeight = 320,
            Props =
            [
                P("findingCount", "Finding Count", PropType.Int,  3,    cat: "Content"),
                P("pendingCount", "Pending Count", PropType.Int,  2,    cat: "State"),
                P("readOnly",     "Read Only",     PropType.Bool, false, cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDeadlineCalculator", Category = "Complex", DisplayName = "Deadline Calculator",
            DefaultWidth = 420, DefaultHeight = 260,
            Props =
            [
                P("baseDate",     "Base Date",     PropType.String, "2026-07-03", cat: "Content"),
                P("resultDate",   "Result Date",   PropType.String, "2026-07-20", cat: "Content"),
                P("showForm",     "Show Form",     PropType.Bool,   true,         cat: "Appearance"),
                P("showProtocol", "Show Protocol", PropType.Bool,   true,         cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmAuditLogViewer", Category = "Complex", DisplayName = "Audit Log Viewer",
            DefaultWidth = 640, DefaultHeight = 420,
            Props =
            [
                P("eventCount",   "Event Count",   PropType.Int,  100, cat: "Content"),
                P("showTimeline", "Show Timeline", PropType.Bool, true, cat: "Appearance"),
                P("showFilters",  "Show Filters",  PropType.Bool, true, cat: "Appearance"),
                P("showExport",   "Show Export",   PropType.Bool, true, cat: "Appearance"),
                P("integrity",    "Integrity",     PropType.Enum, "verified",
                    opts: ["verified", "failed", "none"], cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmAIPrompt", Category = "Complex", DisplayName = "AI Prompt",
            DefaultWidth = 320, DefaultHeight = 120,
            Props =
            [
                P("placeholder", "Placeholder", PropType.String, "Ask anything…", cat: "Content"),
                P("showChips",   "Show Chips",   PropType.Bool,   true,            cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmWidgetSelector", Category = "Complex", DisplayName = "Widget Selector",
            DefaultWidth = 280, DefaultHeight = 200,
            Props =
            [
                P("columns",     "Columns",      PropType.Int, 3, cat: "Appearance"),
                P("widgetCount", "Widget Count", PropType.Int, 6, cat: "Appearance"),
            ]
        };

        // ── GROUP (internal container, not shown in toolbox) ──────────────────
        yield return new WireframeComponentSchema
        {
            Type = "__group__", Category = "Layout", DisplayName = "Group",
            DefaultWidth = 200, DefaultHeight = 150,
            Props =
            [
                P("label", "Label", PropType.String, "Group", cat: "Content"),
            ]
        };
    }

    // ── COLOR ─────────────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> Color()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmColorPicker", Category = "Color", DisplayName = "Color Picker",
            DefaultWidth = 140, DefaultHeight = 36,
            Props =
            [
                P("label", "Label", PropType.String, "Color", cat: "Content"),
                P("value", "Value", PropType.Color,  "#3b82f6", cat: "State"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFlatColorPicker", Category = "Color", DisplayName = "Flat Color Picker",
            DefaultWidth = 200, DefaultHeight = 140,
            Props =
            [
                P("value",   "Value",   PropType.Color, "#3b82f6", cat: "State"),
                P("columns", "Columns", PropType.Int,   8,         cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmColorPalette", Category = "Color", DisplayName = "Color Palette",
            DefaultWidth = 200, DefaultHeight = 40,
            Props =
            [
                P("swatches", "Swatches", PropType.Int, 8, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmColorGradient", Category = "Color", DisplayName = "Color Gradient",
            DefaultWidth = 200, DefaultHeight = 120,
            Props =
            [
                P("startColor", "Start Color", PropType.Color, "#3b82f6", cat: "State"),
                P("endColor",   "End Color",   PropType.Color, "#8b5cf6", cat: "State"),
            ]
        };
    }

    // ── EDITORS & APPS ────────────────────────────────────────────────────────

    private static IEnumerable<WireframeComponentSchema> EditorsAndApps()
    {
        yield return new WireframeComponentSchema
        {
            Type = "TmChat", Category = "Editors & Apps", DisplayName = "Chat",
            DefaultWidth = 320, DefaultHeight = 400,
            Props =
            [
                P("messageCount",  "Message Count",  PropType.Int,  4,    cat: "Appearance"),
                P("showInput",     "Show Input",     PropType.Bool, true, cat: "Appearance"),
                P("showReactions", "Show Reactions", PropType.Bool, true, cat: "Appearance"),
                P("showThreads",   "Show Threads",   PropType.Bool, true, cat: "Appearance"),
                P("showReceipts",  "Show Receipts",  PropType.Bool, true, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmSpreadsheet", Category = "Editors & Apps", DisplayName = "Spreadsheet",
            DefaultWidth = 480, DefaultHeight = 320,
            Props =
            [
                P("rows",    "Rows",    PropType.Int, 8,  cat: "Appearance"),
                P("columns", "Columns", PropType.Int, 6,  cat: "Appearance"),
                P("sheetCount", "Sheet Count", PropType.Int, 2, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmGantt", Category = "Editors & Apps", DisplayName = "Gantt Chart",
            DefaultWidth = 520, DefaultHeight = 300,
            Props =
            [
                P("taskCount", "Task Count", PropType.Int, 5, cat: "Appearance"),
                P("period",    "Period",     PropType.Enum,"week", cat: "Appearance",
                    opts: ["day","week","month","quarter"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmGanttPortfolio", Category = "Editors & Apps", DisplayName = "Gantt Portfolio",
            DefaultWidth = 520, DefaultHeight = 300,
            Props =
            [
                P("projectCount", "Project Count", PropType.Int, 3, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmPivotTable", Category = "Editors & Apps", DisplayName = "Pivot Table",
            DefaultWidth = 420, DefaultHeight = 280,
            Props =
            [
                P("rows",    "Rows",    PropType.Int, 4, cat: "Appearance"),
                P("columns", "Columns", PropType.Int, 4, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmTreeList", Category = "Editors & Apps", DisplayName = "Tree List",
            DefaultWidth = 360, DefaultHeight = 260,
            Props =
            [
                P("rowCount",    "Row Count",    PropType.Int, 6, cat: "Appearance"),
                P("columnCount", "Column Count", PropType.Int, 3, cat: "Appearance"),
                P("depth",       "Depth",        PropType.Int, 2, cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDiagramEditor", Category = "Editors & Apps", DisplayName = "Diagram Editor",
            DefaultWidth = 520, DefaultHeight = 340,
            Props =
            [
                P("title",     "Title",      PropType.String, "Diagram",   cat: "Content"),
                P("nodeCount", "Node Count", PropType.Int,    4,           cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDocumentEditor", Category = "Editors & Apps", DisplayName = "Document Editor",
            DefaultWidth = 480, DefaultHeight = 360,
            Props =
            [
                P("title",     "Title",      PropType.String, "Document", cat: "Content"),
                P("showRuler", "Show Ruler", PropType.Bool,   true,       cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmNotionEditor", Category = "Editors & Apps", DisplayName = "Notion Editor",
            DefaultWidth = 520, DefaultHeight = 360,
            Props =
            [
                P("title",       "Title",        PropType.String, "Page Title", cat: "Content"),
                P("showSidebar", "Show Sidebar", PropType.Bool,   true,         cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmNotionPage", Category = "Editors & Apps", DisplayName = "Notion Page",
            DefaultWidth = 420, DefaultHeight = 360,
            Props =
            [
                P("title",      "Title",      PropType.String, "Page Title", cat: "Content"),
                P("blockCount", "Block Count",PropType.Int,    5,            cat: "Appearance"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmModelingEditor", Category = "Editors & Apps", DisplayName = "Modeling Editor",
            DefaultWidth = 520, DefaultHeight = 340,
            Props =
            [
                P("title", "Title", PropType.String, "Model", cat: "Content"),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmFileManager", Category = "Editors & Apps", DisplayName = "File Manager",
            DefaultWidth = 480, DefaultHeight = 320,
            Props =
            [
                P("path",        "Current Path",  PropType.String, "/Documents", cat: "Content"),
                P("viewMode",    "View Mode",     PropType.Enum,   "grid",       cat: "Appearance",
                    opts: ["grid","list"]),
            ]
        };

        yield return new WireframeComponentSchema
        {
            Type = "TmDocumentManager", Category = "Editors & Apps", DisplayName = "Document Manager",
            DefaultWidth = 480, DefaultHeight = 320,
            Props =
            [
                P("rowCount",    "Row Count",     PropType.Int,  6,    cat: "Appearance"),
                P("showPreview", "Show Preview",  PropType.Bool, true, cat: "Appearance"),
            ]
        };
    }
}
