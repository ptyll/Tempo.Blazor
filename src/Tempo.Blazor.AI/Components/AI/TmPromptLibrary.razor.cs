using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// A library of prompt templates: the list on the left, the picked template's variables and the assembled prompt on
/// the right (on a phone the pick drills down to the detail, with a Back action that returns the focus to the row).
/// <para>
/// The placeholders (<c>{{variable}}</c>) are found and substituted by <see cref="PromptTemplateParser"/> — this
/// component owns no parsing and no evaluation, so what the preview shows is exactly what
/// <see cref="OnUse"/> hands the host: the same pass produces both. A template without an explicit
/// <see cref="PromptTemplate.Variables"/> gets one field per placeholder; a template that declares them is
/// authoritative (label, description, default, required flag, kind and select options).
/// </para>
/// <para>
/// The assembly is always previewed, including the parts that are still empty: an unfilled variable is rendered as
/// its literal placeholder with a localized "Unfilled" marker, so a hole is never silent. "Use" is offered only
/// with a bound <see cref="OnUse"/> and is ARIA-disabled (<c>aria-disabled="true"</c>, not the native attribute)
/// while the prompt is incomplete: it stays in the tab order, so pressing it says what is missing instead of the
/// reason being unreachable — and a prompt with a hole can never reach a host through this component, because both
/// the handler and <see cref="PromptTemplateBuildResult.ToUse"/> refuse it.
/// </para>
/// <para>
/// The component is transport-agnostic and owns no persistence: the host keeps the templates, the draft values and
/// whatever "Use" means. Editing is opt-in through <see cref="OnCreate"/>, <see cref="OnEdit"/> and
/// <see cref="OnDelete"/> — a button is rendered only when its callback is bound, so a read-only host offers no
/// edit action at all. Copy writes the assembled prompt with the browser clipboard and announces the result in the
/// component's ONE polite live region (the validation summary is announced there too; the fields themselves carry
/// their error text through <c>aria-describedby</c>, so nothing is said twice).
/// </para>
/// </summary>
public partial class TmPromptLibrary : TmComponentBase
{
    /// <summary>
    /// The category-filter value that selects the templates with no category. A host that persisted a filter reads
    /// the same constant, so the sentinel is part of the contract rather than an implementation detail. The value
    /// is deliberately not a plausible category name.
    /// </summary>
    public const string UncategorisedCategoryValue = "__uncategorised";

    /// <summary>The category-filter value that shows every template.</summary>
    private const string AllCategoriesValue = "";

    /// <summary>The per-instance id prefix, so several libraries can share one page.</summary>
    private readonly string _id = $"tm-prompt-library-{Guid.NewGuid():n}";

    /// <summary>The row elements by template id — the focus hand-back targets after a phone drill-down closes.
    /// </summary>
    private readonly Dictionary<string, ElementReference> _rowRefs = new(StringComparer.Ordinal);

    /// <summary>The variables the reader has touched; a field's error is withheld until then.</summary>
    private readonly HashSet<string> _touched = new(StringComparer.Ordinal);

    private ElementReference _rootRef;
    private ElementReference _backRef;

    private List<PromptTemplate> _visible = [];
    private List<SegmentedOption<string>> _categoryOptions = [];
    private Dictionary<string, string?> _values = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string?>? _valuesParameter;

    private PromptTemplateBuildResult _result = new();

    private string _searchText = string.Empty;
    private string _category = AllCategoriesValue;
    private string? _selectedId;
    private string? _selectedIdParameter;
    private string? _notifiedSelectedId;
    private string? _pendingFocusRowId;

    private bool _drilldown;
    private bool _attemptedUse;

    /// <summary>
    /// Set when a selection change replaced the draft with the new template's defaults. The host is told after the
    /// render (an <see cref="OnParametersSet"/> notification would re-enter the parameter set and loop), so a
    /// <c>@bind-Values</c> host never keeps the previous template's values.
    /// </summary>
    private bool _valuesResetPending;

    private string _announcement = string.Empty;
    private int _announcementSequence;
    private string? _announcedValidation;

    private TmLayoutMode _resolvedLayout = TmLayoutMode.Desktop;
    private FocusTarget _focusTarget;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The templates to show, in the host's order. Null is treated as an empty library (the localized empty state).
    /// The component never mutates the list: editing is reported through <see cref="OnCreate"/>,
    /// <see cref="OnEdit"/> and <see cref="OnDelete"/> and the host replaces the list.
    /// </summary>
    [Parameter] public IReadOnlyList<PromptTemplate>? Templates { get; set; }

    /// <summary>
    /// The id of the template shown in the detail, two-way bound with <see cref="SelectedTemplateIdChanged"/>.
    /// Null (the default) makes the component select the first template, so the detail pane is never empty; the
    /// selection it made is reported, so a host binding stays in sync. An id that names no template is ignored.
    /// </summary>
    [Parameter] public string? SelectedTemplateId { get; set; }

    /// <summary>Raised when the selection changes — a row picked by the reader, or the initial selection the
    /// component made. The value is the <see cref="PromptTemplate.Id"/> (null when the library is empty).</summary>
    [Parameter] public EventCallback<string?> SelectedTemplateIdChanged { get; set; }

    /// <summary>
    /// The draft values by variable name, two-way bound with <see cref="ValuesChanged"/>. A host can pre-fill the
    /// fields (and persist a draft) by setting it; the component reports every edit and every reset as a fresh
    /// snapshot. A null/whitespace value counts as empty, exactly like the parser treats it. A name the selected
    /// template does not declare is ignored by the assembly.
    /// </summary>
    [Parameter] public IReadOnlyDictionary<string, string?>? Values { get; set; }

    /// <summary>Raised with a snapshot of the draft values after every edit, and after the component resets them
    /// when the selection changes.</summary>
    /// <remarks>
    /// The reset is reported AFTER the render rather than from <c>OnParametersSet</c>: raising the callback while the
    /// parameters are being applied re-enters the parameter set and loops. A reset is not reported when the host
    /// supplied the draft for the new selection in the same parameter set — there was nothing lost.
    /// </remarks>
    [Parameter] public EventCallback<IReadOnlyDictionary<string, string?>> ValuesChanged { get; set; }

    /// <summary>
    /// Raised when the reader presses Use on a COMPLETE prompt. Not bound (the default) means the action is not
    /// rendered at all. The payload carries the assembled prompt, the template it came from and the effective
    /// values (the reader's input plus every default that stood in for an empty field).
    /// </summary>
    [Parameter] public EventCallback<PromptTemplateUse> OnUse { get; set; }

    /// <summary>Raised when the reader asks for a new template. Not bound (the default) means the "New template"
    /// action is not rendered. The host owns the editor and the list.</summary>
    [Parameter] public EventCallback OnCreate { get; set; }

    /// <summary>Raised with the selected template when the reader asks to edit it. Not bound (the default) means
    /// no Edit action is rendered.</summary>
    [Parameter] public EventCallback<PromptTemplate> OnEdit { get; set; }

    /// <summary>Raised with the selected template when the reader asks to delete it. Not bound (the default) means
    /// no Delete action is rendered.</summary>
    [Parameter] public EventCallback<PromptTemplate> OnDelete { get; set; }

    /// <summary>
    /// The visible heading. Null (the default) uses the localized "Prompt library", so the surface is always named;
    /// the same text names the root group. A whitespace-only value counts as null.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>; the root keeps its name either way.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>
    /// The heading's level in the page outline, clamped to 2–6. Default 3. The detail's title renders one level
    /// below it (also clamped), so the two panes nest in the outline.
    /// </summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// The search field's placeholder, and its accessible name. Null (the default) uses the localized "Search
    /// templates".
    /// </summary>
    [Parameter] public string? SearchPlaceholder { get; set; }

    /// <summary>
    /// The layout the presentation resolves from. <see cref="TmLayoutMode.Auto"/> (the default) measures the
    /// component's own container; a forced value never measures, so a test or a host can pin desktop, tablet or the
    /// phone drill-down without a DOM.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The mode rendered before the first measurement (static SSR, prerender, the first frame). Default
    /// <see cref="TmLayoutMode.Desktop"/>; a cascaded app-level context still wins.</summary>
    [Parameter] public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived state ────────────────────────────────────────────────────────

    /// <summary>The templates the host supplied, as a non-null list.</summary>
    private IReadOnlyList<PromptTemplate> AllTemplates => Templates ?? [];

    /// <summary>Whether there is anything to show at all.</summary>
    private bool HasTemplates => AllTemplates.Count > 0;

    /// <summary>The selected template, or null when nothing (or nothing valid) is selected.</summary>
    private PromptTemplate? SelectedTemplate
        => _selectedId is { Length: > 0 } id
            ? AllTemplates.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.Ordinal))
            : null;

    /// <summary>The visible heading text: the host's, or the localized default.</summary>
    private string TitleText
        => string.IsNullOrWhiteSpace(Title) ? Loc["TmPromptLibrary_Title"] : Title!.Trim();

    /// <summary>Whether the heading is rendered.</summary>
    private bool HasTitle => ShowTitle && TitleText.Length > 0;

    /// <summary>The name the root group is announced by — the same text the heading shows.</summary>
    private string EffectiveName => TitleText;

    /// <summary>The DOM id of the visible heading.</summary>
    private string TitleId => $"{_id}-title";

    /// <summary>The DOM id of the detail's title (the detail region's <c>aria-labelledby</c> target).</summary>
    private string DetailTitleId => $"{_id}-detail-title";

    /// <summary>The DOM id of the preview's visible label (the preview group's <c>aria-labelledby</c> target).</summary>
    private string PreviewLabelId => $"{_id}-preview-label";

    /// <summary>The DOM id of the validation summary, so the unavailable action can be described by it.</summary>
    private string ValidationSummaryId => $"{_id}-validation";

    /// <summary>
    /// What describes the Use action: the validation summary once it is on the page (the reader pressed the action
    /// and it explained itself), or null while there is nothing to point at.
    /// </summary>
    private string? UseDescriptionId => ValidationSummary is null ? null : ValidationSummaryId;

    /// <summary>Whether the resolved presentation is the phone drill-down.</summary>
    private bool IsMobile => (LayoutMode != TmLayoutMode.Auto ? LayoutMode : _resolvedLayout) == TmLayoutMode.Mobile;

    /// <summary>The root classes: the base class plus the host's own.</summary>
    private string RootClass
        => string.IsNullOrWhiteSpace(Class) ? "tm-prompt-library" : $"tm-prompt-library {Class}";

    /// <summary>The attributes that put the stable search test id on the search input.</summary>
    private Dictionary<string, object> SearchAttributes
        => new() { ["data-testid"] = TestId("prompt-library-search") };

    /// <summary>The no-results sentence, naming the term the reader typed.</summary>
    private string NoResultsText => Loc["TmPromptLibrary_NoResults", _searchText.Trim()];

    /// <summary>The heading, built as a fragment so its tag comes from <see cref="HeadingLevel"/>.</summary>
    private RenderFragment TitleHeading => builder =>
    {
        builder.OpenElement(0, HeadingTag(HeadingLevel));
        builder.AddAttribute(1, "class", "tm-prompt-library__title");
        builder.AddAttribute(2, "id", TitleId);
        builder.AddContent(3, TitleText);
        builder.CloseElement();
    };

    /// <summary>The detail's title, one level below the library heading.</summary>
    private RenderFragment DetailHeading => builder =>
    {
        builder.OpenElement(0, HeadingTag(HeadingLevel + 1));
        builder.AddAttribute(1, "class", "tm-prompt-library__detail-title");
        builder.AddAttribute(2, "id", DetailTitleId);
        builder.AddContent(3, SelectedTemplate?.Title ?? string.Empty);
        builder.CloseElement();
    };

    /// <summary>Whether the reader has made an attempt worth reporting: a touched field or a pressed Use.</summary>
    private bool IsAttempted => _attemptedUse || _touched.Count > 0;

    /// <summary>
    /// The visible validation summary, or null while there is nothing to say. It lists only the MISSING REQUIRED
    /// variables — the one error the form itself can produce — by the labels the reader sees.
    /// </summary>
    private string? ValidationSummary
    {
        get
        {
            if (!IsAttempted)
            {
                return null;
            }

            var missing = _result.Errors
                .Where(error => error.Kind == PromptTemplateValidationErrorKind.MissingRequired)
                .Select(error => LabelFor(error.VariableName))
                .ToList();

            return missing.Count == 0
                ? null
                : Loc["TmPromptLibrary_MissingRequired", string.Join(", ", missing)];
        }
    }

    /// <summary>
    /// The preview: literal runs as text, a filled variable as a <c>mark</c>, and an unfilled one as a
    /// <c>mark</c> that also carries the localized "Unfilled" word for a screen reader (the visual cue is the
    /// modifier class, which is never the only signal).
    /// </summary>
    private RenderFragment PreviewBody => builder =>
    {
        var sequence = 0;
        foreach (var segment in _result.Segments)
        {
            if (!segment.IsVariable)
            {
                builder.AddContent(sequence++, segment.Text);
                continue;
            }

            builder.OpenElement(sequence++, "mark");
            builder.AddAttribute(sequence++, "class", segment.IsEmpty
                ? "tm-prompt-library__token tm-prompt-library__token--empty"
                : "tm-prompt-library__token");

            if (segment.IsEmpty)
            {
                builder.OpenElement(sequence++, "span");
                builder.AddAttribute(sequence++, "class", "tm-sr-only");
                builder.AddContent(sequence++, Loc["TmPromptLibrary_Unfilled"] + " ");
                builder.CloseElement();
            }

            builder.AddContent(sequence++, segment.Text);
            builder.CloseElement();
        }
    };

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // Read BEFORE the adopters move it: a host that changes the selection and hands the draft for that selection
        // in the same pass keeps its draft, whichever order the two parameters are assigned in. Clearing Values
        // (null) is not a draft, so that pass still resets to the new template's defaults.
        var hostSuppliedValues = Values is not null && !ReferenceEquals(Values, _valuesParameter);

        AdoptSelectionParameter(hostSuppliedValues);
        AdoptValuesParameter();
        RebuildCategories();
        PruneRowRefs();
        FilterTemplates();
        EnsureSelection();
        RefreshResult();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!string.Equals(_selectedId, _notifiedSelectedId, StringComparison.Ordinal))
        {
            _notifiedSelectedId = _selectedId;
            if (SelectedTemplateIdChanged.HasDelegate)
            {
                await SelectedTemplateIdChanged.InvokeAsync(_selectedId);
            }
        }

        // The draft was replaced by the new template's defaults. The host has to hear about it here and not in
        // OnParametersSet: raising the callback there would re-enter the parameter set. A host that feeds the
        // snapshot back as Values adopts it next pass with the same content, so this reports exactly once.
        if (_valuesResetPending)
        {
            _valuesResetPending = false;
            if (ValuesChanged.HasDelegate)
            {
                await ValuesChanged.InvokeAsync(
                    _values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            }
        }

        if (_focusTarget == FocusTarget.None)
        {
            return;
        }

        var target = _focusTarget;
        var pendingRow = _pendingFocusRowId;
        _focusTarget = FocusTarget.None;
        _pendingFocusRowId = null;

        // The root is the fallback for every target that is no longer on the page (the detail whose Back button did
        // not render, a row the host dropped). Only a row asks the browser to scroll: a list can be longer than the
        // screen, so the row may sit outside the viewport and the focus ring has to be brought into view
        // (WCAG 2.4.7); the detail and the root are where the reader already is, so they keep the scroll position.
        ElementReference element = _rootRef;
        var preventScroll = true;

        if (target == FocusTarget.Detail && _backRef.Id is not null)
        {
            element = _backRef;
        }
        else if (target == FocusTarget.Row
                 && pendingRow is { } rowId
                 && _rowRefs.TryGetValue(rowId, out var row))
        {
            element = row;
            preventScroll = false;
        }

        try
        {
            await element.FocusAsync(preventScroll);
        }
        catch (JSDisconnectedException)
        {
            // Blazor Server: the circuit dropped, so there is no focus to move.
        }
        catch (JSException)
        {
            // The element left the DOM between the render and the focus call.
        }
        catch (InvalidOperationException)
        {
            // No renderer, or the element reference is no longer attached.
        }
    }

    // ── Parameters and state ─────────────────────────────────────────────────

    /// <summary>
    /// Adopts a selection the host made. <paramref name="hostSuppliedValues"/> is a draft the host handed over in the
    /// same pass; it belongs to the selection the host also named, so the draft is kept instead of being replaced by
    /// the new template's defaults.
    /// </summary>
    private void AdoptSelectionParameter(bool hostSuppliedValues)
    {
        if (string.Equals(SelectedTemplateId, _selectedIdParameter, StringComparison.Ordinal))
        {
            return;
        }

        _selectedIdParameter = SelectedTemplateId;

        if (string.IsNullOrEmpty(SelectedTemplateId))
        {
            _selectedId = null;
            _drilldown = false;
            return;
        }

        if (!string.Equals(_selectedId, SelectedTemplateId, StringComparison.Ordinal))
        {
            ApplySelection(SelectedTemplateId!, resetValues: !hostSuppliedValues);
        }
    }

    private void AdoptValuesParameter()
    {
        if (ReferenceEquals(Values, _valuesParameter))
        {
            return;
        }

        _valuesParameter = Values;

        if (Values is null)
        {
            return;
        }

        _values = Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Selects the first template when nothing valid is selected. A selection the component makes itself reports
    /// itself (the host binding then knows what the detail shows) and does not discard values the host supplied in
    /// the same parameter set — a draft pre-filled for the first template has to survive.
    /// </summary>
    private void EnsureSelection()
    {
        var templates = AllTemplates;

        if (_selectedId is { Length: > 0 } id
            && templates.Any(template => string.Equals(template.Id, id, StringComparison.Ordinal)))
        {
            return;
        }

        if (templates.Count == 0)
        {
            _selectedId = null;
            return;
        }

        var hostSuppliedValues = _values.Count > 0;
        ApplySelection(templates[0].Id, resetValues: !hostSuppliedValues);

        if (hostSuppliedValues)
        {
            return;
        }

        _touched.Clear();
        _attemptedUse = false;
        _announcedValidation = null;
    }

    /// <summary>
    /// Selects a template. <paramref name="resetValues"/> replaces the draft with the new template's defaults and
    /// marks it for reporting after the render, so a <c>@bind-Values</c> host never keeps the previous template's
    /// values; the notification itself lives in <see cref="OnAfterRenderAsync"/> (see
    /// <see cref="_valuesResetPending"/>).
    /// </summary>
    private void ApplySelection(string templateId, bool resetValues)
    {
        _selectedId = templateId;

        if (!resetValues)
        {
            return;
        }

        _values = DefaultsFor(SelectedTemplate);
        _touched.Clear();
        _attemptedUse = false;
        _announcedValidation = null;
        _valuesResetPending = true;
        RefreshResult();
    }

    /// <summary>Recomputes the preview after a state change the parameter set does not see.</summary>
    private void RefreshResult()
    {
        _result = SelectedTemplate is { } template
            ? PromptTemplateParser.Build(template, _values)
            : new PromptTemplateBuildResult();
    }

    private Dictionary<string, string?> DefaultsFor(PromptTemplate? template)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (template is null)
        {
            return values;
        }

        foreach (var variable in PromptTemplateParser.ResolveVariables(template))
        {
            values[variable.Name] = variable.DefaultValue ?? string.Empty;
        }

        return values;
    }

    /// <summary>
    /// Drops the focus targets of templates the host removed. The row reference is otherwise kept for the session,
    /// so a long-lived library would leak one entry per deleted template (and a focus hand-back could target an
    /// element that no longer exists).
    /// </summary>
    private void PruneRowRefs()
    {
        if (_rowRefs.Count == 0)
        {
            return;
        }

        var live = AllTemplates.Select(template => template.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _rowRefs.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _rowRefs.Remove(stale);
        }
    }

    private void RebuildCategories()
    {
        var templates = AllTemplates;
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var uncategorised = 0;

        foreach (var template in templates)
        {
            if (string.IsNullOrWhiteSpace(template.Category))
            {
                uncategorised++;
                continue;
            }

            if (!counts.TryGetValue(template.Category, out var count))
            {
                order.Add(template.Category);
                counts[template.Category] = 0;
            }

            counts[template.Category] = count + 1;
        }

        var options = new List<SegmentedOption<string>>(order.Count + 2)
        {
            new()
            {
                Label = Loc["TmPromptLibrary_AllCategories"],
                Value = AllCategoriesValue,
                Count = templates.Count,
            },
        };

        foreach (var category in order)
        {
            options.Add(new SegmentedOption<string>
            {
                Label = category,
                Value = category,
                Count = counts[category],
            });
        }

        if (uncategorised > 0)
        {
            options.Add(new SegmentedOption<string>
            {
                Label = Loc["TmPromptLibrary_Uncategorised"],
                Value = UncategorisedCategoryValue,
                Count = uncategorised,
            });
        }

        _categoryOptions = options;

        if (_category.Length > 0 && options.All(option => !string.Equals(option.Value, _category, StringComparison.Ordinal)))
        {
            _category = AllCategoriesValue;
        }
    }

    private void FilterTemplates()
    {
        var query = _searchText.Trim();

        _visible = AllTemplates
            .Where(MatchesCategory)
            .Where(template => query.Length == 0 || Matches(template, query))
            .ToList();
    }

    private bool MatchesCategory(PromptTemplate template)
    {
        if (_category.Length == 0)
        {
            return true;
        }

        return string.Equals(_category, UncategorisedCategoryValue, StringComparison.Ordinal)
            ? string.IsNullOrWhiteSpace(template.Category)
            : string.Equals(template.Category, _category, StringComparison.Ordinal);
    }

    /// <summary>
    /// A free-text match over everything a reader picks a template by, ignoring case and diacritics (so "uvitani"
    /// finds "Uvítání") — the same comparison the repo's other filters use.
    /// </summary>
    private static bool Matches(PromptTemplate template, string query)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

        return compare.IndexOf(template.Title, query, options) >= 0
            || compare.IndexOf(template.Description ?? string.Empty, query, options) >= 0
            || compare.IndexOf(template.Category ?? string.Empty, query, options) >= 0
            || compare.IndexOf(template.Body, query, options) >= 0;
    }

    /// <summary>The category a row shows: the host's own word, or the localized "Uncategorised".</summary>
    private string CategoryText(PromptTemplate template)
        => string.IsNullOrWhiteSpace(template.Category)
            ? Loc["TmPromptLibrary_Uncategorised"]
            : template.Category!;

    private bool IsSelected(PromptTemplate template)
        => string.Equals(_selectedId, template.Id, StringComparison.Ordinal);

    private string LabelFor(string variableName)
        => _result.Variables
            .FirstOrDefault(variable => string.Equals(variable.Name, variableName, StringComparison.Ordinal))
            ?.DisplayLabel ?? variableName;

    // ── The variable form ────────────────────────────────────────────────────

    /// <summary>One variable as a <c>TmFormField</c> wrapping the control its kind asks for.</summary>
    private RenderFragment VariableField(PromptVariable variable) => builder =>
    {
        var sequence = 0;
        builder.OpenComponent<TmFormField>(sequence++);
        builder.AddAttribute(sequence++, nameof(TmFormField.Id), FieldId(variable));
        builder.AddAttribute(sequence++, nameof(TmFormField.For), FieldId(variable));
        builder.AddAttribute(sequence++, nameof(TmFormField.Label), variable.DisplayLabel);
        builder.AddAttribute(sequence++, nameof(TmFormField.Required), variable.IsRequired);
        builder.AddAttribute(sequence++, nameof(TmFormField.HelpText), variable.Description);
        builder.AddAttribute(sequence++, nameof(TmFormField.ErrorMessage), FieldError(variable));
        builder.AddAttribute(sequence++, nameof(TmFormField.AnnounceError), false);
        builder.AddAttribute(sequence++, nameof(TmFormField.Class), "tm-prompt-library__field");
        builder.AddAttribute(sequence, nameof(TmFormField.ChildContent), (RenderFragment)(child =>
        {
            var childSequence = 0;
            switch (variable.Kind)
            {
                case PromptVariableKind.Multiline:
                    child.OpenComponent<TmTextArea>(childSequence++);
                    child.AddAttribute(childSequence++, nameof(TmTextArea.Id), FieldId(variable));
                    child.AddAttribute(childSequence++, nameof(TmTextArea.Value), FieldValue(variable.Name));
                    child.AddAttribute(childSequence++, nameof(TmTextArea.Immediate), true);
                    child.AddAttribute(
                        childSequence++,
                        nameof(TmTextArea.ValueChanged),
                        EventCallback.Factory.Create<string>(this, value => SetValueAsync(variable.Name, value)));
                    child.AddAttribute(childSequence++, nameof(TmTextArea.AdditionalAttributes), VariableAttributes(variable.Name));
                    child.CloseComponent();
                    break;

                case PromptVariableKind.Select when variable.Options is { Count: > 0 }:
                    child.OpenComponent<TmSelect<string>>(childSequence++);
                    child.AddAttribute(childSequence++, nameof(TmSelect<string>.Id), FieldId(variable));
                    child.AddAttribute(childSequence++, nameof(TmSelect<string>.Value), FieldValue(variable.Name));
                    child.AddAttribute(childSequence++, nameof(TmSelect<string>.Options), SelectOptions(variable));
                    child.AddAttribute(childSequence++, nameof(TmSelect<string>.PlaceholderSelectable), true);
                    child.AddAttribute(
                        childSequence++,
                        nameof(TmSelect<string>.Placeholder),
                        Loc["TmPromptLibrary_SelectPlaceholder"]);
                    child.AddAttribute(
                        childSequence++,
                        nameof(TmSelect<string>.ValueChanged),
                        EventCallback.Factory.Create<string?>(this, value => SetValueAsync(variable.Name, value ?? string.Empty)));
                    child.AddAttribute(childSequence++, nameof(TmSelect<string>.SelectAttributes), VariableAttributes(variable.Name));
                    child.CloseComponent();
                    break;

                default:
                    child.OpenComponent<TmTextInput>(childSequence++);
                    child.AddAttribute(childSequence++, nameof(TmTextInput.Id), FieldId(variable));
                    child.AddAttribute(childSequence++, nameof(TmTextInput.Value), FieldValue(variable.Name));
                    child.AddAttribute(childSequence++, nameof(TmTextInput.Required), variable.IsRequired);
                    child.AddAttribute(
                        childSequence++,
                        nameof(TmTextInput.IsValid),
                        FieldError(variable) is null ? null : (bool?)false);
                    child.AddAttribute(
                        childSequence++,
                        nameof(TmTextInput.ValueChanged),
                        EventCallback.Factory.Create<string>(this, value => SetValueAsync(variable.Name, value)));
                    child.AddAttribute(childSequence++, nameof(TmTextInput.AdditionalAttributes), VariableAttributes(variable.Name));
                    child.CloseComponent();
                    break;
            }
        }));
        builder.CloseComponent();
    };

    /// <summary>The <c>data-testid</c>/<c>data-variable</c> pair that lets a host and a test find a field.</summary>
    private Dictionary<string, object> VariableAttributes(string variableName)
        => new()
        {
            ["data-testid"] = TestId("prompt-library-variable"),
            ["data-variable"] = variableName,
        };

    private IReadOnlyList<SelectOption<string>> SelectOptions(PromptVariable variable)
        => (variable.Options ?? [])
            .Select(option => new SelectOption<string>(option, option))
            .ToList();

    /// <summary>The DOM id of a variable's control and its form field (shared, so the label points at the control).
    /// </summary>
    private string FieldId(PromptVariable variable) => $"{_id}-{Slug(variable.Name)}";

    private static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        }

        return builder.ToString();
    }

    private string FieldValue(string variableName)
        => _values.TryGetValue(variableName, out var value) ? value ?? string.Empty : string.Empty;

    private string? FieldError(PromptVariable variable)
    {
        if (!IsAttempted)
        {
            return null;
        }

        return _result.ErrorFor(variable.Name)?.Kind switch
        {
            PromptTemplateValidationErrorKind.MissingRequired
                => Loc["TmPromptLibrary_RequiredError", variable.DisplayLabel],
            PromptTemplateValidationErrorKind.NotAnOption
                => Loc["TmPromptLibrary_NotAnOptionError", variable.DisplayLabel],
            _ => null,
        };
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private async Task OnSearchAsync(string value)
    {
        _searchText = value ?? string.Empty;
        FilterTemplates();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnCategoryAsync(string? value)
    {
        _category = value ?? AllCategoriesValue;
        FilterTemplates();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Picks a template: on a phone it also opens the drill-down and moves the focus into the detail.
    /// </summary>
    private async Task OpenAsync(PromptTemplate template)
    {
        if (!string.Equals(_selectedId, template.Id, StringComparison.Ordinal))
        {
            ApplySelection(template.Id, resetValues: true);
        }

        if (IsMobile)
        {
            _drilldown = true;
            _focusTarget = FocusTarget.Detail;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task BackToListAsync()
    {
        _drilldown = false;
        _pendingFocusRowId = _selectedId;
        _focusTarget = FocusTarget.Row;
        await InvokeAsync(StateHasChanged);
    }

    private async Task SetValueAsync(string variableName, string? value)
    {
        _touched.Add(variableName);

        var next = value ?? string.Empty;
        var changed = !string.Equals(FieldValue(variableName), next, StringComparison.Ordinal);
        if (changed)
        {
            _values[variableName] = next;
            RefreshResult();

            if (ValuesChanged.HasDelegate)
            {
                await ValuesChanged.InvokeAsync(
                    _values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            }
        }

        AnnounceValidationIfChanged();
        await InvokeAsync(StateHasChanged);
    }

    private async Task UseAsync()
    {
        _attemptedUse = true;

        if (!_result.IsValid)
        {
            // The button is disabled on an incomplete prompt, so this is the defensive path a host's imperative
            // call could take. Nothing reaches the host; the reader is told what to fill in.
            AnnounceValidationIfChanged();
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (OnUse.HasDelegate && SelectedTemplate is { } template)
        {
            await OnUse.InvokeAsync(_result.ToUse(template));
        }
    }

    private async Task CopyAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", _result.Prompt);
            Announce(Loc["TmPromptLibrary_Copied"]);
        }
        catch (Exception)
        {
            // A refused clipboard write (permissions, insecure context, a dropped circuit) must not claim success.
            Announce(Loc["TmPromptLibrary_CopyFailed"]);
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task CreateAsync()
    {
        if (OnCreate.HasDelegate)
        {
            await OnCreate.InvokeAsync();
        }
    }

    private async Task EditAsync()
    {
        if (OnEdit.HasDelegate && SelectedTemplate is { } template)
        {
            await OnEdit.InvokeAsync(template);
        }
    }

    private async Task DeleteAsync()
    {
        if (OnDelete.HasDelegate && SelectedTemplate is { } template)
        {
            await OnDelete.InvokeAsync(template);
        }
    }

    // ── Announcements and focus ──────────────────────────────────────────────

    /// <summary>
    /// Writes the live region. The region is persistent and the child is keyed by a counter, so a sentence that
    /// repeats still reaches a screen reader (an identical write into an unchanged node is frequently silent).
    /// </summary>
    private void Announce(string text)
    {
        _announcement = text;
        _announcementSequence++;
    }

    /// <summary>
    /// Announces the validation summary when it CHANGES — so an untouched form stays silent and typing does not
    /// repeat the same sentence on every keystroke.
    /// </summary>
    private void AnnounceValidationIfChanged()
    {
        var summary = ValidationSummary;
        if (summary is null)
        {
            _announcedValidation = null;
            return;
        }

        if (string.Equals(summary, _announcedValidation, StringComparison.Ordinal))
        {
            return;
        }

        _announcedValidation = summary;
        Announce(summary);
    }

    private void OnLayoutResolved(TmLayoutMode mode)
    {
        _resolvedLayout = mode;
    }

    private static string HeadingTag(int level)
        => "h" + Math.Clamp(level, 2, 6).ToString(CultureInfo.InvariantCulture);

    private enum FocusTarget
    {
        None = 0,
        Detail = 1,
        Row = 2,
    }
}
